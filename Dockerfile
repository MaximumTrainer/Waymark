# syntax=docker/dockerfile:1
# ─────────────────────────────────────────────────────────────
# Stage 1: build & publish
# ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first to leverage layer caching for NuGet restore
COPY src/backend/OpenOnboarding.Api/OpenOnboarding.Api.csproj                       OpenOnboarding.Api/
COPY src/backend/OpenOnboarding.Application/OpenOnboarding.Application.csproj       OpenOnboarding.Application/
COPY src/backend/OpenOnboarding.Domain/OpenOnboarding.Domain.csproj                 OpenOnboarding.Domain/
COPY src/backend/OpenOnboarding.Infrastructure/OpenOnboarding.Infrastructure.csproj OpenOnboarding.Infrastructure/

# Restore only production API project (tests not needed in the runtime image)
RUN dotnet restore OpenOnboarding.Api/OpenOnboarding.Api.csproj

# Copy remaining source and publish
COPY src/backend/ .
RUN dotnet publish OpenOnboarding.Api/OpenOnboarding.Api.csproj \
    -c Release \
    --no-restore \
    -o /publish

# ─────────────────────────────────────────────────────────────
# Stage 2: runtime image
# ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create a non-root user for security
RUN adduser --disabled-password --gecos "" appuser
USER appuser

COPY --from=build /publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "OpenOnboarding.Api.dll"]
