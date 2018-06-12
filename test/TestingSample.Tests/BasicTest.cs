using System;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace TestingSample.Tests
{
    public class BasicTests : IClassFixture<CustomWebApplicationFactory>
    {
        public BasicTests(CustomWebApplicationFactory factory)
        {
            Factory = factory;
        }

        public CustomWebApplicationFactory Factory { get; }

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
