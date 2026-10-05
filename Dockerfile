# -------- Build Stage --------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy the app project only. The solution also lists RestaurantMenu.Tests, which isn't
# copied into the image, so restore the app's .csproj rather than the solution.
COPY RestaurantMenu/ ./RestaurantMenu/

# Restore dependencies for the app
RUN dotnet restore RestaurantMenu/RestaurantMenu.csproj

# Copy the rest of the code (if any additional files)
# (optional if everything is already copied)

# Build and publish the project. Name the .csproj explicitly so the build does
# not depend on exactly one project file being present in the directory.
RUN dotnet publish RestaurantMenu/RestaurantMenu.csproj -c Release -o /out

# -------- Runtime Stage --------
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Copy the published app
COPY --from=build /out .

# Expose Render port
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000

# Start the app
ENTRYPOINT ["dotnet", "RestaurantMenu.dll"]
