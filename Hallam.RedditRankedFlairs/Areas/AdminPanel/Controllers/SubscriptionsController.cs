﻿using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Hallam.RedditRankedFlairs.Services;
using Hallam.RedditRankedFlairs.Services.Reddit;

namespace Hallam.RedditRankedFlairs.Areas.AdminPanel.Controllers
{
    [AdminAuthorize]
    public class SubscriptionsController : ApiController
    {
        private readonly IRedditService _reddit;
        private readonly ISubRedditService _subReddits;

        public SubscriptionsController(ISubRedditService subReddits, IRedditService reddit)
        {
            _subReddits = subReddits;
            _reddit = reddit;
        }

        [HttpGet, Route("adminPanel/api/moderatorOf")]
        public async Task<IHttpActionResult> GetModeratorOf()
        {
            try
            {
                return Ok(await _reddit.GetSubRedditsAsync(SubRedditKind.Moderator));
            }
            catch (HttpRequestException)
            {
                return Content(HttpStatusCode.Conflict, "Error communicating with Reddit.");
            }
        }

        [HttpGet, Route("adminPanel/api/subscriptions")]
        public async Task<IHttpActionResult> Get()
        {
            var entries = await _subReddits.GetAllAsync();
            return Ok(from e in entries
                      orderby e.Name
                      select new { name = e.Name, status = "Linked" });
        }

        public class SubscribeDto
        {
            [Required]
            public string Name { get; set; }
        }

        [HttpPost, Route("adminPanel/api/subscribe")]
        public async Task<IHttpActionResult> Subscribe(SubscribeDto model)
        {
            if(!ModelState.IsValid)
                return BadRequest(ModelState);

            var moderatorSubs = await _reddit.GetSubRedditsAsync(SubRedditKind.Moderator);
            var currentSubs = await _subReddits.GetAllAsync();

            if (!moderatorSubs.Contains(model.Name, StringComparer.InvariantCultureIgnoreCase))
            {
                return Conflict("Moderator trait is required.");
            }

            if (currentSubs.Any(s => s.Name.Equals(model.Name, StringComparison.InvariantCultureIgnoreCase)))
            {
                return Conflict("That Sub Reddit is already linked.");
            }

            if (!await _subReddits.AddAsync(model.Name))
            {
                return Conflict("Error linking Sub Reddit.");
            }

            return Ok();
        }

        /*
        [HttpPost, Route("adminPanel/api/unsubscribe")]
        public async Task<IHttpActionResult> Unsubscribe(SubscribeDto model)
        {
            
        } 
        */

        private IHttpActionResult Conflict(string message)
        {
            return Content(HttpStatusCode.Conflict, message);
        }
    }
}
