using System;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace TestingSample.Tests
{
    public class BasicTests : IClassFixture<CustomWebApplicationFactory<Startup>>
    {
        public BasicTests(CustomWebApplicationFactory<Startup> factory)
        {
            Factory = factory;
        }

        public CustomWebApplicationFactory<Startup> Factory { get; }

        [Fact]
        public async Task GetHome()
        {
            // Arrange
            var client = Factory.CreateClient();

            // Act
            var response = await client.GetAsync("/");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
