# See https://aka.ms/customizecontainer to learn how to customize your debug container
# and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# ---------- BASE STAGE ----------
# This stage is used when running from VS in fast mode (default for debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# ---------- BUILD STAGE ----------
# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Setup Artifact Feed Credentials
ARG FEED_USERNAME=PetraCMS

# Install the Azure Artifacts Credential Provider
RUN curl -L https://raw.githubusercontent.com/Microsoft/artifacts-credprovider/master/helpers/installcredprovider.sh  | sh

# Copy project fils and nuget config as distinct layers
COPY --link ["AMAIS_WKFLO_MD_API/src/AMAIS.WKFLO.MD/AMAIS.WKFLO.MD.WebAPI/AMAIS.WKFLO.MD.WebAPI.csproj", "AMAIS_WKFLO_MD_API/src/AMAIS.WKFLO.MD/AMAIS.WKFLO.MD.WebAPI/"]
COPY --link ["AMAIS_WKFLO_MD_API/src/AMAIS.WKFLO.MD/AMAIS.WKFLO.MD.Application/AMAIS.WKFLO.MD.Application.csproj", "AMAIS_WKFLO_MD_API/src/AMAIS.WKFLO.MD/AMAIS.WKFLO.MD.Application/"]
COPY --link ["AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common/AMAIS.WKFLO.Common.csproj", "AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common/"]
COPY --link ["AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.MD.Domain/AMAIS.WKFLO.Common.MD.Domain.csproj", "AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.MD.Domain/"]
COPY --link ["AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.Infrastructure/AMAIS.WKFLO.Common.Infrastructure.csproj", "AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.Infrastructure/"]
COPY --link ["AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.MD.Application/AMAIS.WKFLO.Common.MD.Application.csproj", "AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.MD.Application/"]
COPY --link nuget.config .

# Restore NuGet packages with Azure Artifacts Credential Provider using the feed access token passed as a secret
RUN --mount=type=secret,id=feed_token \
    FEED_ACCESSTOKEN="$(cat /run/secrets/feed_token)" && \
    export VSS_NUGET_EXTERNAL_FEED_ENDPOINTS="{\"endpointCredentials\": [{\"endpoint\":\"https://pkgs.dev.azure.com/PetraCMS/PetraCMS/_packaging/AMAIS_PUM/nuget/v3/index.json\", \"username\":\"${FEED_USERNAME}\", \"password\":\"${FEED_ACCESSTOKEN}\"}, {\"endpoint\":\"https://pkgs.dev.azure.com/PetraCMS/PetraCMS/_packaging/AMAIS_DXP/nuget/v3/index.json\", \"username\":\"${FEED_USERNAME}\", \"password\":\"${FEED_ACCESSTOKEN}\"}]}" && \
    dotnet restore "./AMAIS_WKFLO_MD_API/src/AMAIS.WKFLO.MD/AMAIS.WKFLO.MD.WebAPI/AMAIS.WKFLO.MD.WebAPI.csproj"

# Copy the entire solution
COPY --link . .

# Set working directory to the WebAPI project
WORKDIR "/src/AMAIS_WKFLO_MD_API/src/AMAIS.WKFLO.MD/AMAIS.WKFLO.MD.WebAPI"

# Build the project
RUN dotnet build "./AMAIS.WKFLO.MD.WebAPI.csproj" -c $BUILD_CONFIGURATION -o /app/build

# ---------- PUBLISH STAGE ----------
# This stage is used to publish the service project that will be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./AMAIS.WKFLO.MD.WebAPI.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=true

# ---------- FINAL STAGE ----------
# This stage is used in production or when running from VS in normal mode
# (default when not using the debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AMAIS.WKFLO.MD.WebAPI.dll"]