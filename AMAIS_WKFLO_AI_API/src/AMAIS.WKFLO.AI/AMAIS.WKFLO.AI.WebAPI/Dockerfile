# syntax=docker/dockerfile:1

# ---------- BASE STAGE ----------
# This stage is used when running from VS in fast mode (default for debug configuration)
# Pinned to linux/amd64: the CI pipeline always builds on the "ubuntu-latest" Microsoft-hosted
# pool (amd64-only, see pipelines/global_variables.yml) with no --platform flag on the Docker@2
# build task, and the deployment target (Azure Container Apps) only supports linux/amd64 images.
# The sharp native-codec patch below hard-codes the @img/sharp-linux-x64 / sharp-libvips-linux-x64
# package names, so pin the platform explicitly rather than let it silently resolve to the build
# host's native architecture -- an arm64 host would otherwise pull arm64 base/sharp binaries and
# either fail the patch's guard or (worse) install mismatched native codecs (AB#7228).
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:10.0 AS base

# Switch to root user for installation
USER root

# Install Speech SDK native dependencies and apply OS security patches
RUN apt-get update && apt-get upgrade -y && apt-get install -y --no-install-recommends \
    libasound2t64 \
    libssl3 \
    curl \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Install Node, NPM and NPX
ENV NVM_DIR=/usr/local/.nvm

RUN mkdir -p $NVM_DIR \
    && curl -o- https://raw.githubusercontent.com/nvm-sh/nvm/v0.40.4/install.sh | bash \
    && . "$NVM_DIR/nvm.sh" \
    # Pin the Node major (was floating --lts). When Node 26 became LTS in Oct 2026, --lts silently
    # jumped 24 -> 26, whose linux-x64 binary needs libatomic.so.1 (not in the aspnet:10.0 image),
    # so `node` couldn't start and the image build failed (build 54158). `nvm install 24` still
    # picks up the latest 24.x patch release on every rebuild. Move to a new major deliberately,
    # re-validating the npm/dataforseo-mcp-server patches below against it.
    && nvm install 24 \
    && nvm alias default 24 \
    && nvm use default \
    # Pin npm (was floating @latest) for reproducible builds. As of this pin, npm's own
    # bundled tar (7.5.19) and brace-expansion (5.0.7) are still vulnerable to
    # CVE-2026-14257 / GHSA-r292-9mhp-454m even at 12.0.2 -- both are patched below the
    # same way the undici patch further down handles the same class of problem (AB#6786).
    && npm install -g npm@12.0.2 \
    && ln -s "$NVM_DIR/versions/node/$(nvm version default)" "$NVM_DIR/current"

ENV PATH="$NVM_DIR/current/bin:$PATH"

# Patch npm's bundled tar to a non-vulnerable version (GHSA-r292-9mhp-454m: uncontrolled recursion in mapHas/filesFilter causing an uncatchable stack-overflow DoS)
WORKDIR /tmp/tar-patch
RUN npm pack tar@7.5.22 \
    && tar -xzf tar-7.5.22.tgz \
    && rm -rf "$NVM_DIR/current/lib/node_modules/npm/node_modules/tar" \
    && cp -r package "$NVM_DIR/current/lib/node_modules/npm/node_modules/tar" \
    && rm -rf /tmp/tar-patch

# Patch npm's bundled brace-expansion to a non-vulnerable version (CVE-2026-14257: DoS via unbounded expansion length causing an out-of-memory process crash).
# Bumped to 5.0.12 for CVE-2026-102276 [HIGH] (fixed in 5.0.10), CVE-2026-102278 [HIGH]
# (fixed in 5.0.11) and CVE-2026-102277 (fixed in 5.0.12) (AB#7579)
WORKDIR /tmp/brace-expansion-patch
RUN npm pack brace-expansion@5.0.12 \
    && tar -xzf brace-expansion-5.0.12.tgz \
    && rm -rf "$NVM_DIR/current/lib/node_modules/npm/node_modules/brace-expansion" \
    && cp -r package "$NVM_DIR/current/lib/node_modules/npm/node_modules/brace-expansion" \
    && rm -rf /tmp/brace-expansion-patch

# Patch npm's bundled undici to a non-vulnerable version (CVE-2026-13697 [HIGH]: cache
# cross-user disclosure, CVE-2026-16728: retry Content-Length desync, CVE-2026-14643:
# cache whitespace bypass, CVE-2026-16729: cookie attribute injection, CVE-2026-15157:
# blob-type CRLF injection -- all fixed in undici 7.29.0, a security-only release) (AB#6845).
# Bumped to 7.29.1 for CVE-2026-85024: WebSocket permessage-deflate size-limit cleanup
# removes the inflate stream's error listener, so a later inflate error goes unhandled and
# crashes the process -- fixed in undici 7.29.1 (AB#7523)
# Bumped to 7.30.1 for CVE-2026-109869 [HIGH] (fetch() crash on excessive content encodings),
# CVE-2026-109841 (SRI bypass via non-lowercase algorithm tokens) and CVE-2026-109924
# (EventSource memory exhaustion) -- all fixed in undici 7.30.1 (AB#8044)
WORKDIR /tmp/undici-patch
RUN npm pack undici@7.30.1 \
    && tar -xzf undici-7.30.1.tgz \
    && rm -rf "$NVM_DIR/current/lib/node_modules/npm/node_modules/undici" \
    && cp -r package "$NVM_DIR/current/lib/node_modules/npm/node_modules/undici" \
    && rm -rf /tmp/undici-patch

# Patch npm's bundled ip-address to a non-vulnerable version (CVE-2026-69192 [HIGH],
# CVE-2026-54272, CVE-2026-69198). Dependency chain: npm's bundled make-fetch-happen ->
# @npmcli/agent -> socks-proxy-agent -> socks -> ip-address, resolved to the vulnerable
# 10.2.0 at the time this image was built. Fixed in ip-address 10.4.0 (AB#6845).
# Bumped to 10.5.1 for CVE-2026-101910 (Address6.isPrivate misses the NAT64 local-use
# 64:ff9b:1::/48 range) and CVE-2026-101913 (Address6.isLinkLocal only matches fe80::/64
# instead of fe80::/10) -- both fixed in ip-address 10.5.1 (AB#7523).
# Bumped to 10.7.1 for CVE-2026-101911 and CVE-2026-101912 -- both fixed in ip-address
# 10.7.1 (AB#7579)
# Bumped to 10.7.4 for CVE-2026-111997 (DoS via excessive percent characters in IPv6
# address input) -- fixed in ip-address 10.7.4 (AB#8027)
WORKDIR /tmp/ip-address-patch
RUN npm pack ip-address@10.7.4 \
    && tar -xzf ip-address-10.7.4.tgz \
    && rm -rf "$NVM_DIR/current/lib/node_modules/npm/node_modules/ip-address" \
    && cp -r package "$NVM_DIR/current/lib/node_modules/npm/node_modules/ip-address" \
    && rm -rf /tmp/ip-address-patch

# Patch npm's bundled postcss-selector-parser to a non-vulnerable version (CVE-2026-104844:
# splitWord() parsing is quadratic in the number of class/ID indexes, so a crafted selector
# can block a synchronous parser thread -- DoS). Dependency chain: npm -> @npmcli/query ->
# postcss-selector-parser, bundled at the vulnerable 7.1.4. Fixed in postcss-selector-parser
# 7.1.6. Bumping npm doesn't fix this: the latest npm (12.2.0) still bundles 7.1.4 (AB#7709)
WORKDIR /tmp/postcss-selector-parser-patch
RUN npm pack postcss-selector-parser@7.1.6 \
    && tar -xzf postcss-selector-parser-7.1.6.tgz \
    && rm -rf "$NVM_DIR/current/lib/node_modules/npm/node_modules/postcss-selector-parser" \
    && cp -r package "$NVM_DIR/current/lib/node_modules/npm/node_modules/postcss-selector-parser" \
    && rm -rf /tmp/postcss-selector-parser-patch

# Patch npm's bundled glob and minimatch to non-vulnerable versions (CVE-2026-118773: glob's
# abort branches don't return, so cancelled traversals keep walking the filesystem -- DoS; fixed
# in glob 14.0.2. CVE-2026-118764: minimatch's pattern pre-processing re-expands crafted **/..
# segments in a fixpoint loop -- CPU/memory DoS; fixed in minimatch 11.0.1). npm bundles glob
# 13.0.6 and minimatch 10.2.5, and bumping npm doesn't fix this: the latest npm (12.2.1) still
# bundles both. Both fixes are new majors with new dependencies, so each is nested under the
# patched package's own node_modules instead of replacing npm's shared copies:
#   - glob 14 needs path-scurry ^3 (npm bundles 2.0.2, used only by glob)
#   - minimatch 11 adds @isaacs/nonrecursive-glob-match (not bundled by npm)
# Their other dependencies (minipass, lru-cache, balanced-match, and brace-expansion 5.0.12 from
# the patch above) are already bundled at compatible versions. Validated against npm 12.0.2 on
# Node 24: workspaces install, pack with negated globs, cache verify, diff, global install, npx
# and query all behave the same as stock npm. The guard also checks brace-expansion, since
# minimatch 11 needs >=5.0.12 from the patch above. `npm ls -g` reports glob/minimatch as
# "invalid" against npm's own ^13/^10 ranges -- expected for this patch, no runtime effect (AB#8220).
WORKDIR /tmp/glob-minimatch-patch
RUN NPM_MODULES="$NVM_DIR/current/lib/node_modules/npm/node_modules" \
    && npm pack glob@14.0.2 path-scurry@3.0.0 minimatch@11.0.4 @isaacs/nonrecursive-glob-match@1.0.3 \
    && tar -xzf glob-14.0.2.tgz \
    && rm -rf "$NPM_MODULES/glob" && cp -r package "$NPM_MODULES/glob" && rm -rf package \
    && tar -xzf path-scurry-3.0.0.tgz \
    && mkdir -p "$NPM_MODULES/glob/node_modules" \
    && cp -r package "$NPM_MODULES/glob/node_modules/path-scurry" && rm -rf package \
    && tar -xzf minimatch-11.0.4.tgz \
    && rm -rf "$NPM_MODULES/minimatch" && cp -r package "$NPM_MODULES/minimatch" && rm -rf package \
    && tar -xzf isaacs-nonrecursive-glob-match-1.0.3.tgz \
    && mkdir -p "$NPM_MODULES/minimatch/node_modules/@isaacs" \
    && cp -r package "$NPM_MODULES/minimatch/node_modules/@isaacs/nonrecursive-glob-match" && rm -rf package \
    && node -e "const m=process.argv[1]; \
        const v=(d,p)=>require(require.resolve(p+'/package.json',{paths:[m+'/'+d]})).version; \
        const got={glob:v('cacache','glob'), minimatch:v('@npmcli/arborist','minimatch'), \
          pathScurry:v('glob','path-scurry'), braceExpansion:v('minimatch','brace-expansion')}; \
        console.log(JSON.stringify(got)); \
        if (got.glob!=='14.0.2' || got.minimatch!=='11.0.4' || got.pathScurry!=='3.0.0' || got.braceExpansion!=='5.0.12') process.exit(1); \
        require(m+'/glob'); require(m+'/minimatch');" "$NPM_MODULES" \
    || (echo "ERROR: glob/minimatch patch failed (download, copy or version check against npm's internals) -- update this Dockerfile patch (AB#8220)" && exit 1) \
    && rm -rf /tmp/glob-minimatch-patch

RUN node --version && npm --version

# Pre-install MCP servers invoked via npx at runtime so npx resolves them from the local
# global install instead of cold-fetching from the npm registry on first connection --
# that cold fetch was slow/flaky enough in this container to make the MCP client's first
# connection attempt fail outright.
# 3.1.3 keeps 3.1.1's dependency ranges, so the transitive patches below still apply (AB#7985).
RUN npm install -g dataforseo-mcp-server@3.1.3

# Patch dataforseo-mcp-server's transitive sharp to a non-vulnerable version (GHSA-rgj7-g3m4-5g8c
# [HIGH]: RCE via a vulnerable libheif bundled inside sharp's native AVIF/HEIF decoder when
# processing untrusted input; fixed in sharp 0.35.4). Dependency chain: dataforseo-mcp-server ->
# wrangler -> miniflare -> sharp, pinned to the exact vulnerable 0.35.2 by every recent miniflare
# release (checked through 5.20260908.0-alpha) -- there is no newer wrangler/miniflare compatible
# with dataforseo-mcp-server@3.1.3's `^4.125.0` range that re-resolves past it, so this can't be
# fixed by just letting the build pick up a newer wrangler.
# Unlike the pure-JS patches above, sharp's actual image codec lives in separate native
# optionalDependency packages (@img/sharp-<platform> + @img/sharp-libvips-<platform>) that sharp's
# dist/libvips.cjs resolves via a plain require() by package name with NO runtime version check --
# patching only sharp/ itself would leave the OLD vulnerable compiled libvips/libheif binary in
# place while merely making package.json report the newer version, defeating the fix. All three
# coupled packages must be swapped together (AB#7228).
# Bumped to 0.35.5 for GHSA-wq5f-xc86-pv6w [HIGH] (memory bug in the librsvg bundled in sharp's
# prebuilt libvips, possible RCE on glibc Linux; sharp 0.35.5 ships librsvg 2.63.2), which also
# requires @img/sharp-linux-x64 0.35.5 and @img/sharp-libvips-linux-x64 1.3.4 (AB#7733).
WORKDIR /tmp/sharp-patch
RUN SHARP_ROOT="$NVM_DIR/current/lib/node_modules/dataforseo-mcp-server/node_modules" \
    && test -d "$SHARP_ROOT/sharp" && test -d "$SHARP_ROOT/@img/sharp-linux-x64" && test -d "$SHARP_ROOT/@img/sharp-libvips-linux-x64" \
    || (echo "ERROR: sharp did not hoist to the expected dataforseo-mcp-server/node_modules path -- update this Dockerfile patch (AB#7228)" && exit 1) \
    && npm pack sharp@0.35.5 @img/sharp-linux-x64@0.35.5 @img/sharp-libvips-linux-x64@1.3.4 \
    && tar -xzf sharp-0.35.5.tgz \
    && rm -rf "$SHARP_ROOT/sharp" && cp -r package "$SHARP_ROOT/sharp" && rm -rf package \
    && tar -xzf img-sharp-linux-x64-0.35.5.tgz \
    && rm -rf "$SHARP_ROOT/@img/sharp-linux-x64" && cp -r package "$SHARP_ROOT/@img/sharp-linux-x64" && rm -rf package \
    && tar -xzf img-sharp-libvips-linux-x64-1.3.4.tgz \
    && rm -rf "$SHARP_ROOT/@img/sharp-libvips-linux-x64" && cp -r package "$SHARP_ROOT/@img/sharp-libvips-linux-x64" && rm -rf package \
    && rm -rf /tmp/sharp-patch

# Patch dataforseo-mcp-server's transitive undici to a non-vulnerable version (CVE-2026-85024:
# WebSocket permessage-deflate size-limit cleanup removes the inflate stream's error listener, so
# a later inflate error goes unhandled and crashes the process; fixed in undici 7.29.1).
# Dependency chain: dataforseo-mcp-server -> wrangler -> miniflare -> undici, where both wrangler
# and miniflare pin the exact vulnerable "undici": "7.29.0" (still the case in wrangler 4.143.0 /
# miniflare 5.20260926.0-alpha resolved at the time of this patch), so letting the build pick up a
# newer wrangler doesn't fix it. This is a separate copy from npm's own bundled undici patched
# earlier in this stage (AB#7523).
# Bumped to 7.30.1 for CVE-2026-109869 [HIGH], CVE-2026-109841 and CVE-2026-109924 (AB#8044).
WORKDIR /tmp/dataforseo-undici-patch
RUN UNDICI_ROOT="$NVM_DIR/current/lib/node_modules/dataforseo-mcp-server/node_modules" \
    && test -d "$UNDICI_ROOT/undici" \
    || (echo "ERROR: undici did not hoist to the expected dataforseo-mcp-server/node_modules path -- update this Dockerfile patch (AB#7523)" && exit 1) \
    && npm pack undici@7.30.1 \
    && tar -xzf undici-7.30.1.tgz \
    && rm -rf "$UNDICI_ROOT/undici" \
    && cp -r package "$UNDICI_ROOT/undici" \
    && rm -rf /tmp/dataforseo-undici-patch

# Patch dataforseo-mcp-server's @modelcontextprotocol/sdk to a non-vulnerable version
# (CVE-2026-104850 [HIGH]: the SDK's OAuth client support didn't bind stored/pre-provisioned
# credentials to their authorization server, so a malicious MCP server could name its own
# authorization server and receive the client's refresh_token/client_secret; fixed in 1.31.0).
# Dependency chain: dataforseo-mcp-server -> @modelcontextprotocol/sdk (^1.30.0), deduped with
# agents@0.21.0's exact "1.30.0" pin, so it resolves to the vulnerable 1.30.0 -- and there is no
# newer agents release in dataforseo-mcp-server's ^0.21.0 range, so a rebuild doesn't fix it.
# 1.31.0 has an identical dependency list to 1.30.0, so this is a safe drop-in pure-JS swap (AB#7733).
# Bumped to 1.32.1 for CVE-2026-111767 [HIGH], CVE-2026-111768 [HIGH], CVE-2026-111769 and
# CVE-2026-111771 (all fixed in 1.32.0); 1.32.1's dependency list is identical to 1.31.0's (AB#7985).
WORKDIR /tmp/dataforseo-mcp-sdk-patch
RUN MCP_SDK_DIR="$NVM_DIR/current/lib/node_modules/dataforseo-mcp-server/node_modules/@modelcontextprotocol/sdk" \
    && test -d "$MCP_SDK_DIR" \
    || (echo "ERROR: @modelcontextprotocol/sdk did not hoist to the expected dataforseo-mcp-server/node_modules path -- update this Dockerfile patch (AB#7733)" && exit 1) \
    && npm pack @modelcontextprotocol/sdk@1.32.1 \
    && tar -xzf modelcontextprotocol-sdk-1.32.1.tgz \
    && rm -rf "$MCP_SDK_DIR" \
    && cp -r package "$MCP_SDK_DIR" \
    && rm -rf /tmp/dataforseo-mcp-sdk-patch

# Patch dataforseo-mcp-server's @modelcontextprotocol/client, /server and /core to a non-vulnerable
# version. CVE-2026-104850 (see the sdk patch above) also affects @modelcontextprotocol/client
# <2.2.0 on the 2.x line. agents@0.21.0 declares exact peer dependencies on client/server "2.0.0"
# (npm auto-installs peers), so a rebuild doesn't fix it. client and server each pin
# @modelcontextprotocol/core to their own exact version, so all three must move together; at 2.2.0
# their external dependencies are otherwise identical to 2.0.0, making this a drop-in pure-JS swap (AB#7733).
# Bumped to 2.3.1 for CVE-2026-111768 [HIGH], CVE-2026-111770 [HIGH], CVE-2026-111769 and
# CVE-2026-111771 (all fixed in 2.3.0). The only external dependency change is client's
# eventsource-parser floor moving ^3.0.0 -> ^3.0.8, which the copy installed for the sdk's own
# ^3.0.0 range (resolved to the latest 3.x on a fresh install) already satisfies (AB#7985).
WORKDIR /tmp/dataforseo-mcp-v2-patch
RUN MCP_ROOT="$NVM_DIR/current/lib/node_modules/dataforseo-mcp-server/node_modules/@modelcontextprotocol" \
    && test -d "$MCP_ROOT/client" && test -d "$MCP_ROOT/server" && test -d "$MCP_ROOT/core" \
    || (echo "ERROR: @modelcontextprotocol/client, server or core did not hoist to the expected dataforseo-mcp-server/node_modules path -- update this Dockerfile patch (AB#7733)" && exit 1) \
    && npm pack @modelcontextprotocol/client@2.3.1 @modelcontextprotocol/server@2.3.1 @modelcontextprotocol/core@2.3.1 \
    && tar -xzf modelcontextprotocol-client-2.3.1.tgz \
    && rm -rf "$MCP_ROOT/client" && cp -r package "$MCP_ROOT/client" && rm -rf package \
    && tar -xzf modelcontextprotocol-server-2.3.1.tgz \
    && rm -rf "$MCP_ROOT/server" && cp -r package "$MCP_ROOT/server" && rm -rf package \
    && tar -xzf modelcontextprotocol-core-2.3.1.tgz \
    && rm -rf "$MCP_ROOT/core" && cp -r package "$MCP_ROOT/core" && rm -rf package \
    && rm -rf /tmp/dataforseo-mcp-v2-patch

# Switch back to non-root user for security
USER $APP_UID

# Set working directory and expose ports
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# ---------- BUILD STAGE ----------
# This stage is used to build the service project
# Pinned to linux/amd64 for the same reason as the base stage above -- keep both stages on the
# same explicit platform rather than relying on the build host's native architecture (AB#7228).
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Setup Artifact Feed Credentials
ARG FEED_USERNAME=PetraCMS

# Install the Azure Artifacts Credential Provider
RUN curl -L https://raw.githubusercontent.com/Microsoft/artifacts-credprovider/master/helpers/installcredprovider.sh  | sh

# Copy project fils and nuget config as distinct layers
COPY --link ["AMAIS_WKFLO_AI_API/src/AMAIS.WKFLO.AI/AMAIS.WKFLO.AI.WebAPI/AMAIS.WKFLO.AI.WebAPI.csproj", "AMAIS_WKFLO_AI_API/src/AMAIS.WKFLO.AI/AMAIS.WKFLO.AI.WebAPI/"]
COPY --link ["AMAIS_WKFLO_AI_API/src/AMAIS.WKFLO.AI/AMAIS.WKFLO.AI.Application/AMAIS.WKFLO.AI.Application.csproj", "AMAIS_WKFLO_AI_API/src/AMAIS.WKFLO.AI/AMAIS.WKFLO.AI.Application/"]
COPY --link ["AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common/AMAIS.WKFLO.Common.csproj", "AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common/"]
COPY --link ["AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.MD.Domain/AMAIS.WKFLO.Common.MD.Domain.csproj", "AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.MD.Domain/"]
COPY --link ["AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.Infrastructure/AMAIS.WKFLO.Common.Infrastructure.csproj", "AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.Infrastructure/"]
COPY --link ["AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.AI.Application/AMAIS.WKFLO.Common.AI.Application.csproj", "AMAIS_WKFLO/AMAIS_WKFLO_COMMON/src/AMAIS.WKFLO.Common.AI.Application/"]
COPY --link nuget.config .

# Restore NuGet packages with Azure Artifacts Credential Provider using the feed access token passed as a secret
RUN --mount=type=secret,id=feed_token \
    FEED_ACCESSTOKEN="$(cat /run/secrets/feed_token)" && \
    export VSS_NUGET_EXTERNAL_FEED_ENDPOINTS="{\"endpointCredentials\": [{\"endpoint\":\"https://pkgs.dev.azure.com/PetraCMS/PetraCMS/_packaging/AMAIS_PUM/nuget/v3/index.json\", \"username\":\"${FEED_USERNAME}\", \"password\":\"${FEED_ACCESSTOKEN}\"}, {\"endpoint\":\"https://pkgs.dev.azure.com/PetraCMS/PetraCMS/_packaging/AMAIS_DXP/nuget/v3/index.json\", \"username\":\"${FEED_USERNAME}\", \"password\":\"${FEED_ACCESSTOKEN}\"}]}" && \
    dotnet restore "./AMAIS_WKFLO_AI_API/src/AMAIS.WKFLO.AI/AMAIS.WKFLO.AI.WebAPI/AMAIS.WKFLO.AI.WebAPI.csproj"

# Copy the entire solution
COPY --link . .

# Set working directory to the WebAPI project
WORKDIR "/src/AMAIS_WKFLO_AI_API/src/AMAIS.WKFLO.AI/AMAIS.WKFLO.AI.WebAPI"

# Build the project
RUN dotnet build "./AMAIS.WKFLO.AI.WebAPI.csproj" -c $BUILD_CONFIGURATION -o /app/build

# ---------- PUBLISH STAGE ----------
# This stage is used to publish the service project that will be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./AMAIS.WKFLO.AI.WebAPI.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=true

# ---------- FINAL STAGE ----------
# This stage is used in production or when running from VS in normal mode
# (default when not using the debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AMAIS.WKFLO.AI.WebAPI.dll"]