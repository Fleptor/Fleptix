# ==============================================================================
# Stage 1: Build and Publish
# Uses the .NET SDK image containing compilers, build tools, and NuGet clients.
# This stage restores dependencies, compiles source code, and publishes the app.
# ==============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Step 1.1: Copy project files separately first to leverage Docker layer caching.
# If code changes but dependencies do not, Docker reuses cached NuGet restore layers.
COPY ["src/Fleptix.Core/Fleptix.Core.csproj", "src/Fleptix.Core/"]
COPY ["src/Fleptix.Observer/Fleptix.Observer.csproj", "src/Fleptix.Observer/"]

# Step 1.2: Restore package dependencies across referenced projects.
RUN dotnet restore "src/Fleptix.Observer/Fleptix.Observer.csproj"

# Step 1.3: Copy the remaining source files.
COPY src/ src/

# Step 1.4: Publish the application in Release mode into /app/publish.
# --no-restore skips redundant package resolution since restore ran above.
WORKDIR "/src/src/Fleptix.Observer"
RUN dotnet publish "Fleptix.Observer.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore


# ==============================================================================
# Stage 2: Runtime Environment
# Uses the lightweight ASP.NET Core runtime image (no SDK/build tools).
# This minimizes image footprint, surface area vulnerabilities, and attack vectors.
# ==============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Step 2.1: Copy published binaries and static assets from the build stage.
COPY --from=build /app/publish .

# Step 2.2: Create and switch to a dedicated non-root user.
# Running as non-root is a critical security best practice to prevent privilege
# escalation if the container process is compromised.
RUN groupadd -g 10001 fleptix && \
    useradd -u 10001 -g fleptix -s /bin/false -m fleptix && \
    chown -R fleptix:fleptix /app

# Switch active execution user to non-root fleptix
USER fleptix

# Step 2.3: Expose port 80 and configure ASP.NET Core URL bindings.
ENV ASPNETCORE_HTTP_PORTS=80
EXPOSE 80

# Step 2.4: Define container entrypoint to execute the Observer web host.
ENTRYPOINT ["dotnet", "Fleptix.Observer.dll"]
