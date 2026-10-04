# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files first so `dotnet restore` is cached unless dependencies change.
COPY Nelsar.Axia.Api/Nelsar.Axia.Api.csproj Nelsar.Axia.Api/
COPY Nelsar.Axia.Application/Nelsar.Axia.Application.csproj Nelsar.Axia.Application/
COPY Nelsar.Axia.Infrastructure/Nelsar.Axia.Infrastructure.csproj Nelsar.Axia.Infrastructure/
COPY Nelsar.Axia.Domain/Nelsar.Axia.Domain.csproj Nelsar.Axia.Domain/
RUN dotnet restore Nelsar.Axia.Api/Nelsar.Axia.Api.csproj

COPY Nelsar.Axia.Api/ Nelsar.Axia.Api/
COPY Nelsar.Axia.Application/ Nelsar.Axia.Application/
COPY Nelsar.Axia.Infrastructure/ Nelsar.Axia.Infrastructure/
COPY Nelsar.Axia.Domain/ Nelsar.Axia.Domain/

RUN dotnet publish Nelsar.Axia.Api/Nelsar.Axia.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Base image sets ASPNETCORE_HTTP_PORTS=8080 by default; exposed here for documentation.
EXPOSE 8080

ENTRYPOINT ["dotnet", "Nelsar.Axia.Api.dll"]
