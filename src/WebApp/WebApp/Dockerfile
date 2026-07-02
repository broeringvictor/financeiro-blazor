# syntax=docker/dockerfile:1
# Build a partir da raiz do repositório: docker build -f src/WebApp/WebApp/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copia só os .csproj primeiro para cachear o restore em builds subsequentes.
COPY Financeiro.ServiceDefaults/Financeiro.ServiceDefaults.csproj Financeiro.ServiceDefaults/
COPY src/Services/Services.csproj src/Services/
COPY src/WebApp/WebApp.Client/WebApp.Client.csproj src/WebApp/WebApp.Client/
COPY src/WebApp/WebApp/WebApp.csproj src/WebApp/WebApp/
RUN dotnet restore src/WebApp/WebApp/WebApp.csproj

COPY Financeiro.ServiceDefaults/ Financeiro.ServiceDefaults/
COPY src/Services/ src/Services/
COPY src/WebApp/ src/WebApp/
RUN dotnet publish src/WebApp/WebApp/WebApp.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl só para o HEALTHCHECK abaixo; a imagem aspnet base não traz nenhum cliente HTTP.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Onde o volume do SQLite é montado (ver docker-compose.yml).
VOLUME ["/app/data"]

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://localhost:8080/alive || exit 1

ENTRYPOINT ["dotnet", "WebApp.dll"]
