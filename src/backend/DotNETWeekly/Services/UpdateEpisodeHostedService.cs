using DotNETWeekly.Data;
using DotNETWeekly.Models;
using DotNETWeekly.Options;

using Markdig.Syntax;
using Markdig.Syntax.Inlines;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DotNETWeekly.Services
{
    public class UpdateEpisodeHostedService : IHostedService, IDisposable
    {
        private readonly ILogger<UpdateEpisodeHostedService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IEpisodeService _episodeSerivce;

        private readonly EpisodeSyncOption _episodeSyncOption;

        private PeriodicTimer? _timer;

        private Task? _timerTask;

        public UpdateEpisodeHostedService(
            IHttpClientFactory httpClientFactory,
            IEpisodeService episodeService,
            IOptionsSnapshot<EpisodeSyncOption> episodeSyncOptionAccessor,
            ILogger<UpdateEpisodeHostedService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _episodeSerivce = episodeService;
            _episodeSyncOption = episodeSyncOptionAccessor.Value;
            _logger = logger;
        }

        public void Dispose() => _timer?.Dispose();

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (_episodeSyncOption.Enabled && !string.IsNullOrWhiteSpace(_episodeSyncOption.ContentAPI))
            {
                _logger.LogInformation("Updating episodes");
                _timer = new PeriodicTimer(TimeSpan.FromHours(1));
                _timerTask = Start(cancellationToken);
            }

            return Task.CompletedTask;
        }

        private async Task Start(CancellationToken token)
        {
            if (_timer is null)
            {
                return;
            }
            try
            {
                while(await _timer.WaitForNextTickAsync(token) &&
                    !token.IsCancellationRequested)
                {
                    await Update(token);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Update sync job was cancelled.");
            }
        }

        private async Task Update(CancellationToken token)
        {
            var httpClient = _httpClientFactory.CreateClient("GitHub");
            var httpResponseMessage = await httpClient.GetAsync(_episodeSyncOption.ContentAPI, token);
            if (httpResponseMessage.IsSuccessStatusCode)
            {
                _logger.LogInformation("Fetching the episodes successfully.");
                using var contentStream = await httpResponseMessage.Content.ReadAsStreamAsync();
                var files = await JsonSerializer.DeserializeAsync<GithubFile[]>(contentStream, new JsonSerializerOptions()
                {
                    PropertyNameCaseInsensitive = true,
                }, cancellationToken:token);
                if (files == null || files.Length == 0)
                {
                    _logger.LogInformation("Empty of docs");
                    return;
                }
                files = files.Where(p => p.Name.StartsWith("episode")).ToArray();
                var episodeSummaries = await _episodeSerivce.GetEpisodeSummaries(token);
                await UpdateEpisodes(files, episodeSummaries, token);
            }
            else
            {
                _logger.LogError($"Failed to fetch the episodes. Request URL: {_episodeSyncOption.ContentAPI}, response status code {httpResponseMessage.StatusCode}");
            }
        }

        private async Task UpdateEpisodes(IEnumerable<GithubFile> files,  IEnumerable<EpisodeSummary> episodeSummaries, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(files, nameof(files));
            ArgumentNullException.ThrowIfNull(episodeSummaries, nameof(episodeSummaries));
            IEnumerable<string?> episodeIds = files.Select(p => p.Id);
            IEnumerable<string?> removedIds = episodeSummaries.Where(p => !episodeIds.Contains(p.id)).Select(p => p.id);

            foreach (var removedId in removedIds)
            {
                token.ThrowIfCancellationRequested();
                if (removedId != null)
                {
                    await _episodeSerivce.DeleteEpisodeSummary(removedId, token);
                    await _episodeSerivce.DeleteEpisode(removedId, token);
                }
            }

            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                var httpClient = _httpClientFactory.CreateClient("GitHub");
                var httpResponseMessage = await httpClient.GetAsync(file.Url, token);
                if (httpResponseMessage.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"Fetch {file.Name} successfully");
                    using var contentStream = await httpResponseMessage.Content.ReadAsStreamAsync();
                    var fileContent = await JsonSerializer.DeserializeAsync<GithubFileContent>(contentStream, new JsonSerializerOptions()
                    {
                        PropertyNameCaseInsensitive = true,
                    }, token);
                    if (fileContent == null || fileContent.Content == null)
                    {
                        _logger.LogWarning($"Failed to read {file.Name} content");
                        continue;
                    }

                    fileContent.Content = Encoding.UTF8.GetString(Convert.FromBase64String(fileContent.Content));
                    var episodeSummary = episodeSummaries.FirstOrDefault(p => p.id == file.Id);
                    if (episodeSummary == null)
                    {
                        var digist = ComputeDigist(fileContent.Content);
                        var imageLink = GetFirstOrDefaultImage(fileContent.Content);
                        await _episodeSerivce.AddEpsidoeSummary(new EpisodeSummary
                        {
                            id = file.Id,
                            Title = file.Title,
                            Digest = digist,
                            Image = imageLink,
                            CreateTime = DateTime.UtcNow,
                        }, token);
                        await _episodeSerivce.AddEpisode(new Episode
                        {
                            id = file.Id,
                            Content = fileContent.Content,
                            Title = file.Title,
                            CreateTime = DateTime.UtcNow,
                        }, token);
                    }
                    else
                    {
                        var digist = ComputeDigist(fileContent.Content);
                        if (episodeSummary.Digest != digist)
                        {
                            var imageLink = GetFirstOrDefaultImage(fileContent.Content) ?? string.Empty;
                            if (file.Id != null)
                            {
                                await _episodeSerivce.UpdateEpisodeSummary(file.Id, new EpisodeSummary
                                {
                                    id= file.Id,
                                    Title = file.Title,
                                    Digest = digist,
                                    Image = imageLink,
                                    CreateTime = DateTime.UtcNow,
                                }, token);
                                await _episodeSerivce.UpdateEpisode(file.Id, new Episode
                                {
                                    id = file.Id,
                                    Content = fileContent.Content,
                                    Title = file.Title,
                                    CreateTime = DateTime.UtcNow,
                                }, token);
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogError($"Failed to fetch {file.Name}. Status code {httpResponseMessage.StatusCode}");
                }
            }

            
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_timerTask is null)
            {
                return;
            }

            _logger.LogInformation("Stopping updating the episode");
            await _timerTask;

        }

        private static string ComputeDigist(string content)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
            StringBuilder builder = new();
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }
            return builder.ToString();
        }

        private static string? GetFirstOrDefaultImage(string content)
        {
            var doc = Markdig.Markdown.Parse(content);
            var link = doc.Descendants<ParagraphBlock>().SelectMany(x => 
                x.Inline?.Descendants<LinkInline>() ?? Enumerable.Empty<LinkInline>())
                .FirstOrDefault(l => l.IsImage);
            return link?.Url; 
        }

        class GithubFile
        {
            private static readonly Regex regex = new(@"episode-(?<index>\d+)\.md");

            private string? _name;
            public string Name
            {
                get => _name ?? string.Empty; 
                set
                {
                    _name = value;
                    ParseEpisode(value);
                }
            }

            public string? Url { get; set; }

            public string? Type { get; set; }

            public string? Id { get; private set; }

            public string? Title { get; private set; }

            private void ParseEpisode(string name)
            {
                var match = regex.Match(name);
                if (match.Success)
                {
                    var index = int.Parse(match.Groups["index"].Value);
                    Id = index.ToString();
                    Title =  $".NET 周刊第 {index} 期";
                }
            }

        }

        class GithubFileContent
        {
            public string? Content { get; set; }
        }
    }
}
