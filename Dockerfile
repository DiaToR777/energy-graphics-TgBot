# Використовуємо SDK для збірки
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Копіюємо ТІЛЬКИ файл проекту бота (ігноруємо .sln, щоб не тягнути тести)
COPY ["GrafikSvitlaBot/GrafikSvitlaBot.csproj", "GrafikSvitlaBot/"]

# Відновлюємо залежності тільки для цього проекту
RUN dotnet restore "GrafikSvitlaBot/GrafikSvitlaBot.csproj"

# Копіюємо всі інші файли
COPY . .

# Переходимо в папку бота і публікуємо
WORKDIR "/src/GrafikSvitlaBot"
RUN dotnet publish "GrafikSvitlaBot.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Фінальний образ (легкий)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "GrafikSvitlaBot.dll"]
