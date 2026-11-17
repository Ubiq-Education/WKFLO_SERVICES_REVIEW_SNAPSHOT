FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Setup Artifact Feed Credentials
ARG FEED_USERNAME=PetraCMS

# Install the Azure Artifacts Credential Provider
RUN curl -L https://raw.githubusercontent.com/Microsoft/artifacts-credprovider/master/helpers/installcredprovider.sh  | sh

COPY --link ["AMAIS_WKFLO_WORKER/src/AMAIS.WKFLO.Worker/AMAIS.WKFLO.Worker.WebAPI/AMAIS.WKFLO.Worker.WebAPI.csproj", "AMAIS_WKFLO_WORKER/src/AMAIS.WKFLO.Worker/AMAIS.WKFLO.Worker.WebAPI/"]
COPY --link ["AMAIS_WKFLO_WORKER/src/AMAIS.WKFLO.Worker/AMAIS.WKFLO.Worker.Application/AMAIS.WKFLO.Worker.Application.csproj", "AMAIS_WKFLO_WORKER/src/AMAIS.WKFLO.Worker/AMAIS.WKFLO.Worker.Application/"]
COPY --link ["AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common/AMAIS.WKFLO.Common.csproj", "AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common/"]
COPY --link ["AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.MD.Domain/AMAIS.WKFLO.Common.MD.Domain.csproj", "AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.MD.Domain/"]
COPY --link ["AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.Infrastructure/AMAIS.WKFLO.Common.Infrastructure.csproj", "AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.Infrastructure/"]
COPY --link ["AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.AI.Application/AMAIS.WKFLO.Common.AI.Application.csproj", "AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.AI.Application/"]
COPY --link nuget.config .

# Restore NuGet packages with Azure Artifacts Credential Provider using the feed access token passed as a secret
RUN --mount=type=secret,id=feed_token \
    FEED_ACCESSTOKEN="$(cat /run/secrets/feed_token)" && \
    export VSS_NUGET_EXTERNAL_FEED_ENDPOINTS="{\"endpointCredentials\": [{\"endpoint\":\"https://pkgs.dev.azure.com/PetraCMS/PetraCMS/_packaging/AMAIS_PUM/nuget/v3/index.json\", \"username\":\"${FEED_USERNAME}\", \"password\":\"${FEED_ACCESSTOKEN}\"}, {\"endpoint\":\"https://pkgs.dev.azure.com/PetraCMS/PetraCMS/_packaging/AMAIS_DXP/nuget/v3/index.json\", \"username\":\"${FEED_USERNAME}\", \"password\":\"${FEED_ACCESSTOKEN}\"}]}" && \
    dotnet restore "./AMAIS_WKFLO_WORKER/src/AMAIS.WKFLO.Worker/AMAIS.WKFLO.Worker.WebAPI/AMAIS.WKFLO.Worker.WebAPI.csproj"

# Copy the entire solution
COPY --link . .

# Set working directory to the WebAPI project
WORKDIR "/src/AMAIS_WKFLO_WORKER/src/AMAIS.WKFLO.Worker/AMAIS.WKFLO.Worker.WebAPI"

# Build the project
# Restore NuGet packages with Azure Artifacts Credential Provider using the feed access token passed as a secret
RUN --mount=type=secret,id=feed_token \
    FEED_ACCESSTOKEN="$(cat /run/secrets/feed_token)" && \
    export VSS_NUGET_EXTERNAL_FEED_ENDPOINTS="{\"endpointCredentials\": [{\"endpoint\":\"https://pkgs.dev.azure.com/PetraCMS/PetraCMS/_packaging/AMAIS_PUM/nuget/v3/index.json\", \"username\":\"${FEED_USERNAME}\", \"password\":\"${FEED_ACCESSTOKEN}\"}, {\"endpoint\":\"https://pkgs.dev.azure.com/PetraCMS/PetraCMS/_packaging/AMAIS_DXP/nuget/v3/index.json\", \"username\":\"${FEED_USERNAME}\", \"password\":\"${FEED_ACCESSTOKEN}\"}]}" && \
    dotnet build "./AMAIS.WKFLO.Worker.WebAPI.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./AMAIS.WKFLO.Worker.WebAPI.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=true

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AMAIS.WKFLO.Worker.WebAPI.dll"]
