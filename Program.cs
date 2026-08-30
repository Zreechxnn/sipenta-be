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

// Load .env file
DotNetEnv.Env.Load();
builder.Configuration.AddEnvironmentVariables();

// Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();
builder.Host.UseSerilog();

// Configure Kestrel Server Limits for batch document uploads (500 MB)
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 524_288_000; // 500 MB
});

// Configure Form Options for large multidocument multipart uploads
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartBodyLengthLimit = 524_288_000; // 500 MB
    options.MultipartHeadersLengthLimit = int.MaxValue;
    options.ValueCountLimit = 5000;
});

// Konfigurasi Options
builder.Services.Configure<ConnectionStrings>(builder.Configuration.GetSection("ConnectionStrings"));
builder.Services.Configure<ChunkOptions>(builder.Configuration.GetSection("ChunkOptions"));
builder.Services.Configure<OcrOptions>(builder.Configuration.GetSection("Ocr"));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("JwtOptions"));
builder.Services.Configure<GoogleOptions>(builder.Configuration.GetSection("Google"));
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// DbContext PostgreSQL
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString, o => o.UseVector());
});

// Mapster
MapsterConfig.RegisterMappings();
builder.Services.AddMapster();
builder.Services.AddScoped<IMapper, Mapper>();

// FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddFluentValidationAutoValidation();

// SignalR
builder.Services.AddSignalR(hubOptions =>
{
    hubOptions.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10 MB
    hubOptions.EnableDetailedErrors = true;
});

// JWT Authentication
var jwtOptions = builder.Configuration.GetSection("JwtOptions").Get<JwtOptions>();
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
        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtOptions.Key))
    };

    options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "SIAP API", Version = "v1" });
    
    // Add JWT Authentication to Swagger
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

// Health Checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(name: "PostgreSQL");

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        var allowedOrigins = new List<string>
        {
            "https://siap-fe.rechanpage.my.id",
            "https://sipenta-fe.vercel.app",
            "http://localhost:3000"
        };

        policy.WithOrigins(allowedOrigins.ToArray()) // Masukkan array origin ke policy
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});


// Controllers
builder.Services.AddControllers();

// Repositories
builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<IBidangRepository, BidangRepository>();

// Parsers
builder.Services.AddScoped<SIAP.Api.Services.Parsers.IDocumentParser, SIAP.Api.Services.Parsers.Implementations.PdfDocumentParser>();
builder.Services.AddScoped<SIAP.Api.Services.Parsers.IDocumentParser, SIAP.Api.Services.Parsers.Implementations.DocxDocumentParser>();
builder.Services.AddScoped<SIAP.Api.Services.Parsers.IDocumentParser, SIAP.Api.Services.Parsers.Implementations.TxtDocumentParser>();
builder.Services.AddScoped<SIAP.Api.Services.Parsers.IDocumentParserFactory, SIAP.Api.Services.Parsers.DocumentParserFactory>();

// Chunking
builder.Services.AddScoped<SIAP.Api.Services.Chunking.Interfaces.IChunkStrategy, SIAP.Api.Services.Chunking.Implementations.ParagraphChunkStrategy>();
builder.Services.AddScoped<SIAP.Api.Services.Chunking.Interfaces.IChunkStrategy, SIAP.Api.Services.Chunking.Implementations.HeadingChunkStrategy>();
builder.Services.AddScoped<SIAP.Api.Services.Chunking.Interfaces.IChunkStrategy, SIAP.Api.Services.Chunking.Implementations.FixedLengthChunkStrategy>();
builder.Services.AddScoped<SIAP.Api.Services.Chunking.Interfaces.IChunkStrategy, SIAP.Api.Services.Chunking.Implementations.TokenChunkStrategy>();
builder.Services.AddScoped<SIAP.Api.Services.Chunking.Interfaces.IChunkStrategy, SIAP.Api.Services.Chunking.Implementations.LegalDocumentChunkStrategy>();
builder.Services.AddScoped<SIAP.Api.Services.Chunking.Interfaces.IChunkStrategyFactory, SIAP.Api.Services.Chunking.Implementations.ChunkStrategyFactory>();
builder.Services.AddScoped<SIAP.Api.Services.Chunking.Interfaces.IChunkService, SIAP.Api.Services.Chunking.Implementations.ChunkService>();

// Services
builder.Services.AddScoped<IGoogleDriveService, GoogleDriveService>();
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

// Rate Limiting Config
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.Identity?.Name ?? httpContext.Request.Headers.Host.ToString(),
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 100, // 100 requests
                QueueLimit = 2,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                Window = TimeSpan.FromSeconds(10) // per 10 seconds
            }));
    
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync("{\"message\": \"Terlalu banyak permintaan (Rate limit exceeded). Silakan coba lagi nanti.\"}", token);
    };
});

var app = builder.Build();

// Static file serving for extracted document images
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

// Middleware global
app.UseCors("AllowAll");

// HTTPS Redirection & HSTS
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();

// Security Headers
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; connect-src 'self' wss: https://siap-fe.rechanpage.my.id https://sipenta-fe.vercel.app; img-src 'self' data: https:; font-src 'self' data: https:; style-src 'self' 'unsafe-inline' https:; script-src 'self' 'unsafe-inline' https:; frame-ancestors 'none';";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseRateLimiter();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseSwagger();
app.UseSwaggerUI();

// Redirect root to Swagger UI for GET, return "tes" for HEAD
app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapMethods("/", ["HEAD"], () => "tes");

app.UseAuthentication();
app.UseAuthorization();

app.UseSerilogRequestLogging();
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<AppHub>("/hubs/data");

app.Run();