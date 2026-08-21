FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
WORKDIR /app
EXPOSE 8080

# Configure Tesseract paths for Linux environment
ENV Ocr__Provider="Tesseract"
ENV Ocr__ExecutablePath="/usr/bin/tesseract"
ENV Ocr__DataPath="/usr/share/tesseract-ocr/5/tessdata"
ENV Ocr__Language="ind+eng"

# Install Tesseract OCR dan language packs (Indonesia & Inggris) untuk OCR Pipeline
RUN apt-get update && \
    apt-get install -y tesseract-ocr tesseract-ocr-ind tesseract-ocr-eng && \
    rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY ["SIAP.Api.csproj", "./"]
RUN dotnet restore "./SIAP.Api.csproj"
COPY . .
RUN dotnet build "SIAP.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SIAP.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# Using shell execution to read the dynamic PORT environment variable provided by Render
ENTRYPOINT ["sh", "-c", "dotnet SIAP.Api.dll --urls http://0.0.0.0:${PORT:-8080}"]