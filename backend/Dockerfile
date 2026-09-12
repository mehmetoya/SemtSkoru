# Build context is the backend/ directory (see render.yaml: dockerContext).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json ./
COPY SemtSkoru.slnx ./
COPY src/SemtSkoru.Domain/SemtSkoru.Domain.csproj src/SemtSkoru.Domain/
COPY src/SemtSkoru.Application/SemtSkoru.Application.csproj src/SemtSkoru.Application/
COPY src/SemtSkoru.Infrastructure/SemtSkoru.Infrastructure.csproj src/SemtSkoru.Infrastructure/
COPY src/SemtSkoru.Api/SemtSkoru.Api.csproj src/SemtSkoru.Api/
RUN dotnet restore src/SemtSkoru.Api/SemtSkoru.Api.csproj

COPY src/ src/
RUN dotnet publish src/SemtSkoru.Api/SemtSkoru.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Npgsql probes for GSSAPI/Kerberos support at startup regardless of whether the connection
# actually uses it; the base aspnet image doesn't ship it, which otherwise prints a startup
# error ("libgssapi_krb5.so.2: cannot open shared object file") on every boot.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

EXPOSE 8080

# Render (and most free-tier PaaS hosts) inject PORT at container start, not build time, so
# the URL has to be resolved by a shell at runtime rather than baked in via ENV/ASPNETCORE_URLS.
ENTRYPOINT ["/bin/sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} exec dotnet SemtSkoru.Api.dll"]
