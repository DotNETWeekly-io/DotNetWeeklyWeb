using DotNETWeekly.Data;
using DotNETWeekly.Models;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DotNETWeekly.Controllers
{
    [Route("api")]
    [ApiController]
    public class EpisodeController : ControllerBase
    {
        private readonly IEpisodeService _episodeService;

        private readonly IMemoryCache _cache;

        private readonly MemoryCacheEntryOptions cacheOption = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromHours(6));

        public EpisodeController(IEpisodeService episodeService, IMemoryCache cache)
        {
            _episodeService = episodeService;
            _cache = cache;
        }

        [HttpGet("episodes")]
        [AllowAnonymous]
        public async Task<IEnumerable<Episode>> GetAllEpisodes(CancellationToken token)
        {
            return await _episodeService.GetEpisodes(token);
        }

        [HttpGet("episodes/{episodeId:int}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetEpisode(int episodeId, CancellationToken token)
        {
            Episode? episode;
            if (_cache.TryGetValue(episodeId, out episode))
            {
                return Ok(episode);
            }
            episode =  await _episodeService.GetEpisode(episodeId.ToString(), token);
            if (episode == null)
            {
                return NotFound();
            }
            _cache.Set(episodeId, episode, cacheOption);
            return CreatedAtAction(nameof(GetEpisode), episode);
        }

        [HttpGet("episodes/summary")]
        [AllowAnonymous]
        public async Task<IEnumerable<EpisodeSummary>> GetEpisodeSummaries(CancellationToken token)
        {
            return await _episodeService.GetEpisodeSummaries(token);    
        }
    }
}
