# -------- Build Stage --------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy the solution file
COPY RestaurantMenu.sln ./

# Copy the project folder
COPY RestaurantMenu/ ./RestaurantMenu/

# Restore dependencies for the solution
RUN dotnet restore RestaurantMenu.sln

# Copy the rest of the code (if any additional files)
# (optional if everything is already copied)

# Build and publish the project
WORKDIR /app/RestaurantMenu
RUN dotnet publish -c Release -o /out

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
