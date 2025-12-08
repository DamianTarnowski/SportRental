FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY ["SportRental.Admin/SportRental.Admin.csproj", "SportRental.Admin/"]
COPY ["SportRental.Infrastructure/SportRental.Infrastructure.csproj", "SportRental.Infrastructure/"]
COPY ["SportRental.Shared/SportRental.Shared.csproj", "SportRental.Shared/"]
RUN dotnet restore "SportRental.Admin/SportRental.Admin.csproj"
COPY . .
WORKDIR "/src/SportRental.Admin"
RUN dotnet build "SportRental.Admin.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SportRental.Admin.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SportRental.Admin.dll"]




