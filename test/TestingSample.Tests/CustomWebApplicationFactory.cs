using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TestingSample.Tests
{
    public class CustomWebApplicationFactory : WebApplicationFactory<Startup>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            var value = Environment.GetEnvironmentVariable("MY_CONTENT_ROOT");
            if (!string.IsNullOrEmpty(value))
            {
                builder.UseContentRoot(value);
            }
        }
    }
}
