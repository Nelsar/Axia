# syntax=docker/dockerfile:1

# The SDK runs on the machine doing the build and compiles for the platform being built for, so an
# arm64 image needs no emulated SDK — under emulation restore and publish take many times as long,
# when they finish at all. BuildKit sets TARGETARCH (amd64, arm64); `-a` takes it as it is.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Copy csproj files first so `dotnet restore` is cached unless dependencies change.
# Contracts is referenced by Api, Application and Infrastructure alike. Leaving it out is what
# used to break this build: `dotnet restore` reported success — a missing project reference is
# only MSB9008, a warning — and `dotnet publish` then failed with a screen of CS0234.
COPY Nelsar.Axia.Api/Nelsar.Axia.Api.csproj Nelsar.Axia.Api/
COPY Nelsar.Axia.Application/Nelsar.Axia.Application.csproj Nelsar.Axia.Application/
COPY Nelsar.Axia.Infrastructure/Nelsar.Axia.Infrastructure.csproj Nelsar.Axia.Infrastructure/
COPY Nelsar.Axia.Domain/Nelsar.Axia.Domain.csproj Nelsar.Axia.Domain/
COPY Nelsar.Axia.Contracts/Nelsar.Axia.Contracts.csproj Nelsar.Axia.Contracts/
# The version every project inherits. Left out, the image would build as 1.0.0, silently.
COPY Directory.Build.props ./
RUN dotnet restore Nelsar.Axia.Api/Nelsar.Axia.Api.csproj -a "$TARGETARCH"

COPY Nelsar.Axia.Api/ Nelsar.Axia.Api/
COPY Nelsar.Axia.Application/ Nelsar.Axia.Application/
COPY Nelsar.Axia.Infrastructure/ Nelsar.Axia.Infrastructure/
COPY Nelsar.Axia.Domain/ Nelsar.Axia.Domain/
COPY Nelsar.Axia.Contracts/ Nelsar.Axia.Contracts/

RUN dotnet publish Nelsar.Axia.Api/Nelsar.Axia.Api.csproj -c Release -a "$TARGETARCH" -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Not as root. A process holding the directory's bind password and the token signing key has no
# use for root, and anything that took it over would have had the container's. $APP_UID is the
# unprivileged user the .NET images ship (1654). The two places the API writes — the SQLite
# database under /data and the file logs under /app/logs — are created here and given to it, so
# that a named volume mounted on either starts out owned by that user too.
RUN mkdir -p /data /app/logs && chown "$APP_UID" /data /app/logs
USER $APP_UID

# Base image sets ASPNETCORE_HTTP_PORTS=8080 by default; exposed here for documentation.
EXPOSE 8080

ENTRYPOINT ["dotnet", "Nelsar.Axia.Api.dll"]
