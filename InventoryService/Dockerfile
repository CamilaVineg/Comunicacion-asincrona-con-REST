FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

# Las imagenes .NET 8 escuchan en 8080 por defecto. Fijamos 80 para el mapeo de puertos.
ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

# curl se usa para el healthcheck de docker compose
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENTRYPOINT ["dotnet", "InventoryService.dll"]