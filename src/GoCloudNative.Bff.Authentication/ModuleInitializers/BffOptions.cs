using System.Collections.Immutable;
using GoCloudNative.Bff.Authentication.Endpoints;
using GoCloudNative.Bff.Authentication.IdentityProviders;
using GoCloudNative.Bff.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;

namespace GoCloudNative.Bff.Authentication.ModuleInitializers;

public class BffOptions
{
    internal readonly IdpRegistrations IdpRegistrations = new();

    private Action<IReverseProxyBuilder> _applyReverseProxyConfiguration = _ => { };

    private Action<IServiceCollection> _applyClaimsTransformationRegistration = (s) => s.AddTransient<IClaimsTransformation, DefaultClaimsTransformation>();
    
    private Action<IServiceCollection> _applyAuthenticationCallbackHandlerRegistration = (s) => s.AddTransient<IAuthenticationCallbackHandler, DefaultAuthenticationCallbackHandler>();

    internal Uri? CustomHostName = null;

    internal ErrorPage ErrorPage;
        
    internal LandingPage LandingPage;

    /// <summary>
    /// Gets or sets the name of the cookie.
    /// </summary>
    public string SessionCookieName { get; set; } = "bff.cookie";

    /// <summary>
    /// Get or set a value that indicates the amount of time of inactivity after which the session will be abandoned.
    /// </summary>
    public TimeSpan SessionIdleTimeout { get; set; } = TimeSpan.FromMinutes(20);
    
    /// <summary>
    /// Gets ors sets a value which indicates whether or not the redirect_uri will automatically be rewritten to http
    /// instead of https. This feature might come in handy when hosting the software in a Docker image.
    /// </summary>
    public bool AlwaysRedirectToHttps { get; set; } = true;

    /// <summary>
    /// Sets a custom page to redirect to when the authentication on the OIDC Server failed.
    /// The url will be augmented with an additional query string parameter to indicate what error occured.
    /// </summary>
    /// <param name="errorPage">A relative path to the error page</param>
    public void SetAuthenticationErrorPage(string errorPage)
    {
        if (!ErrorPage.TryParse(errorPage, out var value))
        {
            const string errorMessage = "GNC-B-faa80ff1e452: " +
                                        "Cannot initialize GoCloudNative.BFF. " +
                                        "Invalid error page. " +
                                        "The path to the error page must be relative and may not have a querystring.";
            
            throw new NotSupportedException(errorMessage);
        }

        ErrorPage = value;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="landingPage"></param>
    public void SetLandingPage(string landingPage)
    {
        if (!LandingPage.TryParse(landingPage, out var value))
        {
            const string errorMessage = "GNC-B-f30ab76dde63: " +
                                        "Cannot initialize GoCloudNative.BFF. " +
                                        "Invalid landing page. " +
                                        "The path to the landing page must be relative and may not have a querystring.";
            
            throw new NotSupportedException(errorMessage);
        }
        
        LandingPage = value;
    }

    /// <summary>
    /// The GoCloudNative.BFF typically derives the redirect URL from the request context as a default behavior. However, in cases where the hosting of an image with the GoCloudNative.BFF involves proxies or different configurations, the automatically inferred redirect URL may be incorrect. To address this issue, you can utilize the following method to override the default value of the redirect URL.
    /// </summary>
    /// <param name="hostname"></param>
    /// <exception cref="NotSupportedException"></exception>
    public void SetCustomHostName(Uri hostname)
    {
        if (!string.IsNullOrEmpty(hostname.Query))
        {
            throw new NotSupportedException("GCN-B-322cf6ab8a70: " +
                                            "Cannot initialize GoCloudNative.BFF. " +
                                            "Error configuring custom hostname. " +
                                            $"{hostname} is not a valid hostname. " +
                                            "A custom hostname may not have a querystring.");
        }

        CustomHostName = hostname;
    }

    public void RegisterIdentityProvider<TIdentityProvider, TOptions>(TOptions options, string endpointName = "account") 
        where TIdentityProvider : class, IIdentityProvider 
        where TOptions : class
    {
        IdpRegistrations.Register<TIdentityProvider, TOptions>(options, endpointName);
    }

    /// <summary>
    /// By default, the /{0}/me endpoint displays the payload of the ID token, including all the claims. However, there may be situations where it is necessary to display fewer claims or additional claims are required. To customize the claims shown in the /{0}/me endpoint, you can create a custom implementation of the IClaimsTransformation interface and register it using this method. This allows you to control the transformation and selection of claims for the endpoint. 
    /// </summary>
    /// <typeparam name="TClaimsTransformation">The class that augments the output of the /{0}/me endpoint</typeparam>
    public void AddClaimsTransformation<TClaimsTransformation>() where TClaimsTransformation : class, IClaimsTransformation
    {
        _applyClaimsTransformationRegistration = s => s.AddTransient<IClaimsTransformation, TClaimsTransformation>();
    }
    
    /// <summary>
    ///  
    /// </summary>
    /// <typeparam name="TAuthenticationCallbackHandler"></typeparam>
    public void AddAuthenticationCallbackHandler<TAuthenticationCallbackHandler>() where TAuthenticationCallbackHandler : class, IAuthenticationCallbackHandler
    {
        _applyAuthenticationCallbackHandlerRegistration = s => s.AddTransient<IAuthenticationCallbackHandler, TAuthenticationCallbackHandler>();
    }

    /// <summary>
    /// Initialize YARP with the values provided in a configuration-section.
    /// </summary>
    [Obsolete("Will be removed. Migrate to options.ConfigureYarp(..).")]
    public void LoadYarpFromConfig(IConfigurationSection configurationSection)
    {
        _applyReverseProxyConfiguration = b => b.LoadFromConfig(configurationSection);
    }
    
    /// <summary>
    /// YARP is initially set up to forward traffic based on the predefined configuration. However, if you require additional configuration options, you can utilize this method to extend the configuration.
    /// </summary>
    public void ConfigureYarp(Action<IReverseProxyBuilder> configuration)
    {
        _applyReverseProxyConfiguration = configuration;
    }

    /// <summary>
    /// Apply the options to the service collection
    /// </summary>
    public void Apply(IServiceCollection serviceCollection)
    {
        var proxyBuilder = serviceCollection
            .AddTransient(_ => this)
            .AddTransient<IRedirectUriFactory, RedirectUriFactory>()
            .AddReverseProxy();

        _applyReverseProxyConfiguration(proxyBuilder);
        
        IdpRegistrations.Apply(proxyBuilder);
        
        IdpRegistrations.Apply(serviceCollection);

        _applyClaimsTransformationRegistration(serviceCollection);
        _applyAuthenticationCallbackHandlerRegistration(serviceCollection);

        serviceCollection
            .AddDistributedMemoryCache()
            .AddMemoryCache()
            .AddSession(options =>
            {
                options.IdleTimeout = SessionIdleTimeout;
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;    
                options.Cookie.Name = SessionCookieName;
            });
    }
}