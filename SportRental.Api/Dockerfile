FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["SportRental.Api/SportRental.Api.csproj", "SportRental.Api/"]
COPY ["SportRental.Infrastructure/SportRental.Infrastructure.csproj", "SportRental.Infrastructure/"]
COPY ["SportRental.Shared/SportRental.Shared.csproj", "SportRental.Shared/"]
RUN dotnet restore "SportRental.Api/SportRental.Api.csproj"
COPY . .
WORKDIR "/src/SportRental.Api"
RUN dotnet build "SportRental.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SportRental.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SportRental.Api.dll"]




