// Copyright (c) .NET  Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyModel;

namespace TestingSample.Tests
{
    public class CustomWebApplicationFactory<TEntryPoint> : IDisposable where TEntryPoint : class
    {
        private bool _disposed;
        private TestServer _server;
        private Action<IWebHostBuilder> _configuration;
        private IList<HttpClient> _clients = new List<HttpClient>();
        private List<CustomWebApplicationFactory<TEntryPoint>> _derivedFactories =
            new List<CustomWebApplicationFactory<TEntryPoint>>();

        public CustomWebApplicationFactory()
        {
            _configuration = ConfigureWebHost;
        }

        ~CustomWebApplicationFactory()
        {
            Dispose(false);
        }

        public TestServer Server => _server;

        public IReadOnlyList<CustomWebApplicationFactory<TEntryPoint>> Factories => _derivedFactories.AsReadOnly();

        public WebApplicationFactoryClientOptions ClientOptions { get; private set; } = new WebApplicationFactoryClientOptions();

        public CustomWebApplicationFactory<TEntryPoint> WithWebHostBuilder(Action<IWebHostBuilder> configuration) =>
            WithWebHostBuilderCore(configuration);

        internal virtual CustomWebApplicationFactory<TEntryPoint> WithWebHostBuilderCore(Action<IWebHostBuilder> configuration)
        {
            var factory = new DelegatedWebApplicationFactory(
                ClientOptions,
                CreateServer,
                CreateWebHostBuilder,
                GetTestAssemblies,
                ConfigureClient,
                builder =>
                {
                    _configuration(builder);
                    configuration(builder);
                });

            _derivedFactories.Add(factory);

            return factory;
        }

        private void EnsureServer()
        {
            if (_server != null)
            {
                return;
            }

            EnsureDepsFile();


            var builder = CreateWebHostBuilder();
            SetContentRoot(builder);
            _configuration(builder);
            _server = CreateServer(builder);
        }

        private void SetContentRoot(IWebHostBuilder builder)
        {
            var metadataAttributes = GetContentRootMetadataAttributes(
                typeof(TEntryPoint).Assembly.FullName,
                typeof(TEntryPoint).Assembly.GetName().Name);

            string contentRoot = null;
            for (var i = 0; i < metadataAttributes.Length; i++)
            {
                var contentRootAttribute = metadataAttributes[i];
                var contentRootCandidate = Path.Combine(
                    AppContext.BaseDirectory,
                    contentRootAttribute.ContentRootPath);

                var contentRootMarker = Path.Combine(
                    contentRootCandidate,
                    Path.GetFileName(contentRootAttribute.ContentRootTest));

                if (File.Exists(contentRootMarker))
                {
                    contentRoot = contentRootCandidate;
                    break;
                }
            }

            if (contentRoot != null)
            {
                builder.UseContentRoot(contentRoot);
            }
            else
            {
                try
                {
                    builder.UseSolutionRelativeContentRoot(typeof(TEntryPoint).Assembly.GetName().Name);
                }
                catch
                {
                }
            }
        }

        private WebApplicationFactoryContentRootAttribute[] GetContentRootMetadataAttributes(
            string tEntryPointAssemblyFullName,
            string tEntryPointAssemblyName)
        {
            var testAssembly = GetTestAssemblies();
            var metadataAttributes = testAssembly
                .SelectMany(a => a.GetCustomAttributes<WebApplicationFactoryContentRootAttribute>())
                .Where(a => string.Equals(a.Key, tEntryPointAssemblyFullName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(a.Key, tEntryPointAssemblyName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.Priority)
                .ToArray();

            return metadataAttributes;
        }

        protected virtual IEnumerable<Assembly> GetTestAssemblies()
        {
            try
            {
                // The default dependency context will be populated in .net core applications.
                var context = DependencyContext.Default;
                if (context == null || context.CompileLibraries.Count == 0)
                {
                    // The app domain friendly name will be populated in full framework.
                    return new[] { Assembly.Load(AppDomain.CurrentDomain.FriendlyName) };
                }

                var runtimeProjectLibraries = context.RuntimeLibraries
                    .ToDictionary(r => r.Name, r => r, StringComparer.Ordinal);

                // Find the list of projects
                var projects = context.CompileLibraries.Where(l => l.Type == "project");

                var entryPointAssemblyName = typeof(TEntryPoint).Assembly.GetName().Name;

                // Find the list of projects referencing TEntryPoint.
                var candidates = context.CompileLibraries
                    .Where(library => library.Dependencies.Any(d => string.Equals(d.Name, entryPointAssemblyName, StringComparison.Ordinal)));

                var testAssemblies = new List<Assembly>();
                foreach (var candidate in candidates)
                {
                    if (runtimeProjectLibraries.TryGetValue(candidate.Name, out var runtimeLibrary))
                    {
                        var runtimeAssemblies = runtimeLibrary.GetDefaultAssemblyNames(context);
                        testAssemblies.AddRange(runtimeAssemblies.Select(Assembly.Load));
                    }
                }

                return testAssemblies;
            }
            catch (Exception)
            {
            }

            return Array.Empty<Assembly>();
        }

        private void EnsureDepsFile()
        {
            var depsFileName = $"{typeof(TEntryPoint).Assembly.GetName().Name}.deps.json";
            var depsFile = new FileInfo(Path.Combine(AppContext.BaseDirectory, depsFileName));
            if (!depsFile.Exists)
            {
                throw new InvalidOperationException("Can't find deps file");
            }
        }

        protected virtual IWebHostBuilder CreateWebHostBuilder()
        {
            var builder = WebHostBuilderFactory.CreateFromTypesAssemblyEntryPoint<TEntryPoint>(Array.Empty<string>());
            if (builder == null)
            {
                throw new InvalidOperationException("Some error");
            }
            else
            {
                return builder.UseEnvironment("Development");
            }
        }

        protected virtual TestServer CreateServer(IWebHostBuilder builder) => new TestServer(builder);

        protected virtual void ConfigureWebHost(IWebHostBuilder builder)
        {
        }

        public HttpClient CreateClient() =>
            CreateClient(ClientOptions);

        public HttpClient CreateClient(WebApplicationFactoryClientOptions options) =>
            CreateDefaultClient(options.BaseAddress);

        public HttpClient CreateDefaultClient(params DelegatingHandler[] handlers)
        {
            EnsureServer();

            HttpClient client;
            if (handlers == null || handlers.Length == 0)
            {
                client = _server.CreateClient();
            }
            else
            {
                for (var i = handlers.Length - 1; i > 0; i--)
                {
                    handlers[i - 1].InnerHandler = handlers[i];
                }

                var serverHandler = _server.CreateHandler();
                handlers[handlers.Length - 1].InnerHandler = serverHandler;

                client = new HttpClient(handlers[0]);
            }

            _clients.Add(client);

            ConfigureClient(client);

            return client;
        }

        protected virtual void ConfigureClient(HttpClient client)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            client.BaseAddress = new Uri("http://localhost");
        }

        public HttpClient CreateDefaultClient(Uri baseAddress, params DelegatingHandler[] handlers)
        {
            var client = CreateDefaultClient(handlers);
            client.BaseAddress = baseAddress;

            return client;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                foreach (var client in _clients)
                {
                    client.Dispose();
                }

                foreach (var factory in _derivedFactories)
                {
                    factory.Dispose();
                }

                _server?.Dispose();
            }

            _disposed = true;
        }

        private class DelegatedWebApplicationFactory : CustomWebApplicationFactory<TEntryPoint>
        {
            private readonly Func<IWebHostBuilder, TestServer> _createServer;
            private readonly Func<IWebHostBuilder> _createWebHostBuilder;
            private readonly Func<IEnumerable<Assembly>> _getTestAssemblies;
            private readonly Action<HttpClient> _configureClient;

            public DelegatedWebApplicationFactory(
                WebApplicationFactoryClientOptions options,
                Func<IWebHostBuilder, TestServer> createServer,
                Func<IWebHostBuilder> createWebHostBuilder,
                Func<IEnumerable<Assembly>> getTestAssemblies,
                Action<HttpClient> configureClient,
                Action<IWebHostBuilder> configureWebHost)
            {
                ClientOptions = null;
                _createServer = createServer;
                _createWebHostBuilder = createWebHostBuilder;
                _getTestAssemblies = getTestAssemblies;
                _configureClient = configureClient;
                _configuration = configureWebHost;
            }

            protected override TestServer CreateServer(IWebHostBuilder builder) => _createServer(builder);

            protected override IWebHostBuilder CreateWebHostBuilder() => _createWebHostBuilder();

            protected override IEnumerable<Assembly> GetTestAssemblies() => _getTestAssemblies();

            protected override void ConfigureWebHost(IWebHostBuilder builder) => _configuration(builder);

            protected override void ConfigureClient(HttpClient client) => _configureClient(client);

            internal override CustomWebApplicationFactory<TEntryPoint> WithWebHostBuilderCore(Action<IWebHostBuilder> configuration)
            {
                return new DelegatedWebApplicationFactory(
                    ClientOptions,
                    _createServer,
                    _createWebHostBuilder,
                    _getTestAssemblies,
                    _configureClient,
                    builder =>
                    {
                        _configuration(builder);
                        configuration(builder);
                    });
            }
        }
    }
}