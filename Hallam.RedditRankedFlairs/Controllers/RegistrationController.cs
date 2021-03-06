﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Configuration;
using System.Threading.Tasks;
using System.Web.Http;
using Hallam.RedditRankedFlairs.Data;
using Hallam.RedditRankedFlairs.Jobs;
using Hallam.RedditRankedFlairs.Models;
using Hallam.RedditRankedFlairs.Services;
using Hallam.RedditRankedFlairs.Services.Riot;
using Hangfire;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Summoner = Hallam.RedditRankedFlairs.Riot.Summoner;

namespace Hallam.RedditRankedFlairs.Controllers
{
    [Authorize]
    public class RegistrationController : ApiController
    {
        private static readonly Random Random = new Random();
        private const string ReasonString = "Registration";

        protected IRiotService Riot { get; set; }
        protected ISummonerService Summoners { get; set; }
        protected IUserService Users { get; set; }
        protected ValidationService Validation { get; set; }

        public RegistrationController(IRiotService riotService, ISummonerService summonerService,
            IUserService userService, ValidationService validationService)
        {
            Riot = riotService;
            Summoners = summonerService;
            Users = userService;
            Validation = validationService;
        }

        [HttpPost, Route("profile/api/register")]
        public async Task<IHttpActionResult> Register(SummonerModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                // Summoner MUST exist.
                var summoner = await FindSummonerAsync(model.Region, model.SummonerName);

                if (summoner == null)
                    return Conflict("Summoner not found.");

                // Summoner MUST NOT be registered.
                if (await Summoners.IsSummonerRegistered(model.Region, model.SummonerName))
                    return Conflict("Summoner is already registered.");
                
                return Ok(new
                {
                    code = await Validation.GenerateAsync(User.Identity.Name, summoner.Id, model.Region)
                });
            }
            catch (RiotHttpException e)
            {
                switch ((int) e.StatusCode)
                {
                    case 500:
                    case 503:
                        return Conflict("Error communicating with Riot (Service unavailable)");
                }
                throw;
            }
        }

        [HttpPost, Route("profile/api/validate")]
        public async Task<IHttpActionResult> Validate(SummonerModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                // Summoner MUST exist.
                var riotSummoner = await Riot.FindSummonerAsync(model.Region, model.SummonerName);
                var user = await Users.GetUserAsync();

                if (riotSummoner == null)
                {
                    return Conflict("Summoner not found.");
                }

                // Summoner MUST NOT be registered.
                if (await Summoners.IsSummonerRegistered(model.Region, model.SummonerName))
                {
                    return Conflict("Summoner is already registered.");
                }

                var runePages = await Riot.GetRunePagesAsync(model.Region, riotSummoner.Id);
                var code = await Validation.GenerateAsync(User.Identity.Name, riotSummoner.Id, model.Region);
                
                if (!runePages.Any(page => string.Equals(page.Name, code, StringComparison.InvariantCultureIgnoreCase)))
                {
                    return StatusCode(HttpStatusCode.ExpectationFailed);
                }

                // Create the data entity and associate it with the current user
                var currentSummoner =
                    await Summoners.AddSummonerAsync(user, riotSummoner.Id, model.Region, riotSummoner.Name);

                // If the user doesn't have an active summoner, assign the new summoner as active.
                if (await Summoners.GetActiveSummonerAsync(user) == null)
                    await Summoners.SetActiveSummonerAsync(currentSummoner);

                // Send confirmation mail.
                Trace.WriteLine($"user.id={user.Id}, user.name={user.Name}, summoner.Id={currentSummoner.Id}");
                BackgroundJob.Enqueue<ConfirmRegistrationMailJob>(job => job.Execute(user.Id, currentSummoner.Id));

                // Queue up the league update.
                var jobId = BackgroundJob.Enqueue<LeagueUpdateJob>(job => job.Execute(currentSummoner.Id));
                
                // Queue up flair update.
                jobId = BackgroundJob.ContinueWith<FlairUpdateJob>(jobId, job => job.Execute(user.Id));

                // Queue up confirmation mail.
                //jobId = BackgroundJob.ContinueWith<ConfirmFlairUpdatedMailJob>(jobId,
                //    job => job.Execute(user.Id, currentSummoner.Id));

                return Ok();
            }
            catch (RiotHttpException e)
            {
                switch ((int) e.StatusCode)
                {
                    case 500:
                    case 503:
                        return Conflict("Error communicating with Riot (Service unavailable)");
                }
                throw;
            }
        }

        private IHttpActionResult Conflict(string message)
        {
            return Content(HttpStatusCode.Conflict, new HttpError(message));
        }

        private Task<Summoner> FindSummonerAsync(string region, string summonerName)
        {
            return CacheUtil.GetItemAsync($"{region}:{summonerName}".ToLowerInvariant(),
                () => Riot.FindSummonerAsync(region, summonerName));
        }
    }
}
