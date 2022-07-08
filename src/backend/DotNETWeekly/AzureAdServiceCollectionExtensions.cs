
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

using System;
using Microsoft.Extensions.DependencyInjection;

namespace DotNETWeekly
{
    using Options;

    public static class AzureAdServiceCollectionExtensions
    {
        public static AuthenticationBuilder AddAzureAdBearer(this AuthenticationBuilder builder)
            => builder.AddAzureAdBearer(_ => { });

        public static AuthenticationBuilder AddAzureAdBearer(this AuthenticationBuilder builder, Action<AzureAdOptions> configureOptions)
        {
            builder.Services.Configure(configureOptions);
            builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureAzureOptions>();
            builder.AddJwtBearer();
            return builder;
        }

        private class ConfigureAzureOptions : IConfigureNamedOptions<JwtBearerOptions>
        {
            private readonly AzureAdOptions _azureAdOptions;

            public ConfigureAzureOptions(IOptions<AzureAdOptions> azureOptions)
            {
                _azureAdOptions = azureOptions.Value;
            }

            public void Configure(string name, JwtBearerOptions options)
            {
                ArgumentNullException.ThrowIfNull(_azureAdOptions.ClientId, nameof(_azureAdOptions.ClientId));
                ArgumentNullException.ThrowIfNull(_azureAdOptions.Instance, nameof(_azureAdOptions.Instance));
                ArgumentNullException.ThrowIfNull(_azureAdOptions.TenantId, nameof(_azureAdOptions.TenantId));
                options.Audience = _azureAdOptions.ClientId;
                options.Authority = $"{_azureAdOptions.Instance}{_azureAdOptions.TenantId}";
            }

            public void Configure(JwtBearerOptions options)
            {
                Configure(string.Empty, options);
            }
        }
    }
}
