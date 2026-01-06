using GrafikSvitlaBot.Model;
using GrafikSvitlaBot.Services;
using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http;

namespace GraphicsServiceTests
{
    [TestClass]
    public sealed class RateLimiterTests
    {
        [TestMethod]
        public void CheckRateLimit_ShouldDenySubsequentRequests()
        {
            var limiter = new RateLimiter(30);
            long testChatId = 111222333;

            var firstCall = limiter.CheckRateLimit(testChatId);
            var secondCall = limiter.CheckRateLimit(testChatId);

            // Assert (Перевіряємо результат)
            Assert.IsTrue(firstCall.IsAllowed, "Перший запит має бути дозволено");
            Assert.IsFalse(secondCall.IsAllowed, "Другий запит має бути відхилено через ліміт");
            Assert.IsTrue(secondCall.RemainingSeconds > 0, "Має залишитися час очікування");
        }
        [TestMethod]
        public async Task GetGraphicsAsync_StressTest_100ConcurrentCalls()
        {
            // 1. Arrange
            // Імітуємо затримку мережі (ніби GitHub відповідає 1 секунду)
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .Returns(async (HttpRequestMessage request, CancellationToken ct) =>
                {
                    await Task.Delay(1000); // Затримка для створення "черги"
                    return new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.OK,
                        Content = new StringContent("tag_v1")
                    };
                });

            var httpClient = new HttpClient(handlerMock.Object);
            var service = new GraphicsService(httpClient);

            int totalRequests = 100;
            var tasks = new List<Task<List<GrafikData>>>();

            // 2. Act
            Console.WriteLine($"Запуск {totalRequests} запитів одночасно...");
            for (int i = 0; i < totalRequests; i++)
            {
                tasks.Add(service.GetGraphicsAsync(CancellationToken.None));
            }

            // Чекаємо на виконання всіх запитів
            await Task.WhenAll(tasks);

            // 3. Assert
            foreach (var task in tasks)
            {
                Assert.IsNotNull(task.Result, "Один із потоків повернув null!");
            }

            // Перевіряємо, що запит до "last_update.txt" був виконаний лише 1 раз (або мінімальну кількість разів)
            // через те, що перший потік заблокував інші
            handlerMock.Protected().Verify(
                "SendAsync",
                Times.AtMost(5), // В ідеалі 1, але через мілісекунди планувальника може бути трохи більше
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri.ToString().Contains("last_update.txt")),
                ItExpr.IsAny<CancellationToken>()
            );

            Console.WriteLine("Стрес-тест пройдено успішно!");
        }
    }
}
