# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY DomainCopilot.Domain/*.csproj DomainCopilot.Domain/
COPY DomainCopilot.Application/*.csproj DomainCopilot.Application/
COPY DomainCopilot.Infrastructure/*.csproj DomainCopilot.Infrastructure/
COPY DomainCopilot.Api/*.csproj DomainCopilot.Api/
RUN dotnet restore DomainCopilot.Api/DomainCopilot.Api.csproj

COPY . .
RUN dotnet publish DomainCopilot.Api/DomainCopilot.Api.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "DomainCopilot.Api.dll"]