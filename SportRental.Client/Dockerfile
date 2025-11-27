FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["SportRental.Client/SportRental.Client.csproj", "SportRental.Client/"]
COPY ["SportRental.Shared/SportRental.Shared.csproj", "SportRental.Shared/"]
RUN dotnet restore "SportRental.Client/SportRental.Client.csproj"
COPY . .
WORKDIR "/src/SportRental.Client"
RUN dotnet publish "SportRental.Client.csproj" -c Release -o /app/publish

FROM nginx:alpine AS final
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY SportRental.Client/nginx.conf /etc/nginx/nginx.conf
EXPOSE 80




