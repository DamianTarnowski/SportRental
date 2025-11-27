FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["SportRental.MediaStorage/SportRental.MediaStorage.csproj", "SportRental.MediaStorage/"]
COPY ["SportRental.Infrastructure/SportRental.Infrastructure.csproj", "SportRental.Infrastructure/"]
RUN dotnet restore "SportRental.MediaStorage/SportRental.MediaStorage.csproj"
COPY . .
WORKDIR "/src/SportRental.MediaStorage"
RUN dotnet build "SportRental.MediaStorage.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SportRental.MediaStorage.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SportRental.MediaStorage.dll"]




