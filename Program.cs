using Microsoft.AspNetCore.DataProtection;
using FluentValidation;
using FluentValidation.AspNetCore;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SIAP.Api.Configurations;
using SIAP.Api.Data;
using SIAP.Api.Middleware;
using SIAP.Api.Repositories.Interfaces;
using SIAP.Api.Repositories.Implementations;
using SIAP.Api.Services.Interfaces;
using SIAP.Api.Services.Implementations;
using SIAP.Api.Hubs;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

DotNetEnv.Env.Load();
builder.Configuration.AddEnvironmentVariables();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();
builder.Host.UseSerilog();

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 524_288_000;
});

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartBodyLengthLimit = 524_288_000;
    options.MultipartHeadersLengthLimit = int.MaxValue;
    options.ValueCountLimit = 5000;
});

builder.Services.Configure<ConnectionStrings>(builder.Configuration.GetSection("ConnectionStrings"));
builder.Services.Configure<ChunkOptions>(builder.Configuration.GetSection("ChunkOptions"));
builder.Services.Configure<OcrOptions>(builder.Configuration.GetSection("Ocr"));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("JwtOptions"));
builder.Services.Configure<GoogleOptions>(builder.Configuration.GetSection("Google"));
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString, o => o.UseVector());
});

MapsterConfig.RegisterMappings();
builder.Services.AddMapster();
builder.Services.AddScoped<IMapper, Mapper>();

builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddFluentValidationAutoValidation();

builder.Services.AddSignalR(hubOptions =>
{
    hubOptions.MaximumReceiveMessageSize = 10 * 1024 * 1024;
    hubOptions.EnableDetailedErrors = true;
});

builder.Services.AddDataProtection();

var jwtOptions = builder.Configuration.GetSection("JwtOptions").Get<JwtOptions>();
if (jwtOptions == null || string.IsNullOrWhiteSpace(jwtOptions.Key))
{
    throw new InvalidOperationException("Kunci JWT (JwtOptions:Key) belum dikonfigurasi. Pastikan JwtOptions__Key telah disetel di file .env!");
}
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtOptions!.Issuer,
        ValidAudience = jwtOptions.Audience,
        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtOptions.Key)),
        NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier,
        RoleClaimType = System.Security.Claims.ClaimTypes.Role
    };

    options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"].ToString();
            var path = context.HttpContext.Request.Path;

            if (!string.IsNullOrEmpty(accessToken) && 
                accessToken != "hidden-httponly-token" && 
                accessToken != "session-active" && 
                (path.StartsWithSegments("/hubs") || path.StartsWithSegments("/chatHub") || path.Value?.Contains("/images/") == true))
            {
                context.Token = accessToken;
            }

            if (string.IsNullOrEmpty(context.Token))
            {
                if (context.Request.Cookies.TryGetValue("sipenta_token", out var cookieToken) && !string.IsNullOrEmpty(cookieToken))
                {
                    context.Token = cookieToken;
                }
            }

            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "SIAP API", Version = "v1" });
    
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter 'Bearer' [space] and then your valid token in the text input below.\r\n\r\nExample: \"Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9\""
    });
    
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(name: "PostgreSQL");

var allowedOriginsConfig = builder.Configuration["Cors:AllowedOrigins"];
if (string.IsNullOrWhiteSpace(allowedOriginsConfig))
{
    throw new InvalidOperationException("Konfigurasi CORS (Cors:AllowedOrigins) belum disetel di file .env!");
}

var allowedOrigins = allowedOriginsConfig
    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
    .Select(o => o.Trim().TrimEnd('/'))
    .Where(o => !string.IsNullOrEmpty(o))
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
              {
                  if (string.IsNullOrEmpty(origin)) return false;
                  var cleanOrigin = origin.TrimEnd('/');
                  if (allowedOrigins.Contains(cleanOrigin)) return true;
                  try
                  {
                      var uri = new Uri(origin);
                      return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || 
                             uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                             uri.Host.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase) ||
                             uri.Host.Equals("vercel.app", StringComparison.OrdinalIgnoreCase) ||
                             uri.Host.EndsWith(".rechanpage.my.id", StringComparison.OrdinalIgnoreCase) ||
                             uri.Host.Equals("rechanpage.my.id", StringComparison.OrdinalIgnoreCase);
                  }
                  catch
                  {
                      return false;
                  }
              })
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

builder.Services.AddControllers();

builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<IBidangRepository, BidangRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

builder.Services.AddScoped<SIAP.Api.Services.Parsers.IDocumentParser, SIAP.Api.Services.Parsers.Implementations.PdfDocumentParser>();
builder.Services.AddScoped<SIAP.Api.Services.Parsers.IDocumentParser, SIAP.Api.Services.Parsers.Implementations.DocxDocumentParser>();
builder.Services.AddScoped<SIAP.Api.Services.Parsers.IDocumentParser, SIAP.Api.Services.Parsers.Implementations.TxtDocumentParser>();
builder.Services.AddScoped<SIAP.Api.Services.Parsers.IDocumentParserFactory, SIAP.Api.Services.Parsers.DocumentParserFactory>();

builder.Services.AddScoped<SIAP.Api.Services.Chunking.Interfaces.IChunkStrategy, SIAP.Api.Services.Chunking.Implementations.ParagraphChunkStrategy>();
builder.Services.AddScoped<SIAP.Api.Services.Chunking.Interfaces.IChunkStrategy, SIAP.Api.Services.Chunking.Implementations.HeadingChunkStrategy>();
builder.Services.AddScoped<SIAP.Api.Services.Chunking.Interfaces.IChunkStrategy, SIAP.Api.Services.Chunking.Implementations.FixedLengthChunkStrategy>();
builder.Services.AddScoped<SIAP.Api.Services.Chunking.Interfaces.IChunkStrategy, SIAP.Api.Services.Chunking.Implementations.TokenChunkStrategy>();
builder.Services.AddScoped<SIAP.Api.Services.Chunking.Interfaces.IChunkStrategy, SIAP.Api.Services.Chunking.Implementations.LegalDocumentChunkStrategy>();
builder.Services.AddScoped<SIAP.Api.Services.Chunking.Interfaces.IChunkStrategyFactory, SIAP.Api.Services.Chunking.Implementations.ChunkStrategyFactory>();
builder.Services.AddScoped<SIAP.Api.Services.Chunking.Interfaces.IChunkService, SIAP.Api.Services.Chunking.Implementations.ChunkService>();

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();

builder.Services.AddScoped<ISystemConfigService, SystemConfigService>();
builder.Services.AddScoped<GoogleDriveService>();
builder.Services.AddScoped<IGoogleDriveService, CloudStorageManager>();
builder.Services.AddScoped<ICloudStorageService, CloudStorageManager>();

builder.Services.AddScoped<ILoginRateLimiter, LoginRateLimiter>();
builder.Services.AddSingleton<ITokenCipherService, TokenCipherService>();
builder.Services.AddSingleton<IConfigCipherService, ConfigCipherService>();
builder.Services.AddScoped<ISudoElevationService, SudoElevationService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IPdfImageExtractor, PdfImageExtractor>();
builder.Services.AddScoped<IDocumentDetectionService, DocumentDetectionService>();
builder.Services.AddScoped<IOcrProvider, TesseractOcrProvider>();
builder.Services.AddHttpClient<IGroqService, GroqService>();
builder.Services.AddHttpClient<IEmbeddingService, OpenAIEmbeddingService>();
builder.Services.AddSingleton<IDocumentProcessingQueue, DocumentProcessingQueue>();
builder.Services.AddHostedService<DocumentProcessingService>();
builder.Services.AddHostedService<TokenCleanupBackgroundService>();

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.Identity?.Name ?? httpContext.Request.Headers.Host.ToString(),
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 100,
                QueueLimit = 2,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                Window = TimeSpan.FromSeconds(10)
            }));
    
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync("{\"message\": \"Terlalu banyak permintaan (Rate limit exceeded). Silakan coba lagi nanti.\"}", token);
    };
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.ExecuteSqlRaw("UPDATE \"Roles\" SET \"Name\" = 'kepala bagian' WHERE \"Name\" IN ('kasubag', 'kabid');");

        var configService = scope.ServiceProvider.GetRequiredService<ISystemConfigService>();
        await configService.EnsureAllConfigurationsEncryptedAsync();
    }
    catch (Exception ex)
    {
        Serilog.Log.Warning(ex, "Failed to run startup DB role migration or config encryption check");
    }
}

var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "Images");
if (!Directory.Exists(uploadsDir))
{
    Directory.CreateDirectory(uploadsDir);
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsDir),
    RequestPath = "/uploads/images"
});

app.UseForwardedHeaders(new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
});

app.UseCors("AllowAll");

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();

var cspOrigins = string.Join(" ", allowedOrigins);
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Content-Security-Policy"] = $"default-src 'self'; connect-src 'self' wss: {cspOrigins}; img-src 'self' data: https:; font-src 'self' data: https:; style-src 'self' 'unsafe-inline' https:; script-src 'self' 'unsafe-inline' https:; frame-ancestors 'none';";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseRateLimiter();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<CsrfProtectionMiddleware>();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapMethods("/", ["HEAD"], () => "tes");

app.UseAuthentication();
app.UseAuthorization();

app.UseSerilogRequestLogging();
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<AppHub>("/hubs/data");

app.Run();