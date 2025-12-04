# --- ЕТАП ЗБІРКИ (Використовуємо образ з SDK для компіляції) ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Копіюємо файли рішення та проєкту
COPY *.sln ./
COPY GrafikSvitlaBot/GrafikSvitlaBot.csproj GrafikSvitlaBot/

# Відновлюємо залежності
RUN dotnet restore

# Копіюємо решту файлів та публікуємо
COPY . .
WORKDIR /src/GrafikSvitlaBot
# Публікуємо в папку /app/publish
RUN dotnet publish -c Release -o /app/publish --no-restore

# --- ЕТАП ЗАПУСКУ (Використовуємо менший образ для виконання) ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Копіюємо опублікований додаток
COPY --from=build /app/publish .

# Визначаємо точку входу
ENTRYPOINT ["dotnet", "GrafikSvitlaBot.dll"]
