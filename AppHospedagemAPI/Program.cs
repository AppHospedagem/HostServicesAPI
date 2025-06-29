using AppHospedagemAPI.Data;
using AppHospedagemAPI.Endpoints; // Adicione este using
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer; // Adicione este using
using Microsoft.IdentityModel.Tokens; // Adicione este using
using System.Text; // Adicione este using
using System.Security.Claims; // Adicione este using
using Npgsql.EntityFrameworkCore.PostgreSQL; // Adicione este using

var builder = WebApplication.CreateBuilder(args);

// --- Configuração do Swagger/OpenAPI ---
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Adicione a configuração de segurança para o Swagger (Permite testar JWT)
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token in the text input below. Example: \"Bearer eyJhbGciOiJIUzI1Ni...\""
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
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
            new string[] {}
        }
    });
});


// --- Configuração do DbContext ---
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// --- Configuração do JWT (Autenticação e Autorização) ---

// 1. Configurar as opções do JWT a partir do appsettings.json
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"];
var expiryInMinutes = Convert.ToDouble(jwtSettings["ExpiryInMinutes"]);

// Verifica se a SecretKey está configurada
if (string.IsNullOrEmpty(secretKey))
{
    throw new InvalidOperationException("JwtSettings:SecretKey não está configurado em appsettings.json");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false, // Em desenvolvimento, pode ser false. Em produção, true e defina Issuer
        ValidateAudience = false, // Em desenvolvimento, pode ser false. Em produção, true e defina Audience
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(secretKey))
    };
});

// 2. Configurar a Autorização
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("admin", policy => policy.RequireClaim(ClaimTypes.Role, "admin"));
    options.AddPolicy("gerente", policy => policy.RequireClaim(ClaimTypes.Role, "gerente"));
    options.AddPolicy("funcionario", policy => policy.RequireClaim(ClaimTypes.Role, "funcionario"));
    // Você pode adicionar outras políticas conforme necessário, por exemplo, "gerenteOuAdmin"
    // options.AddPolicy("gerenteOuAdmin", policy => policy.RequireRole("gerente", "admin"));
});


var app = builder.Build();

// --- Configuração do Pipeline de Requisições HTTP ---

// Configurar o pipeline de middleware HTTP para Swagger em ambiente de desenvolvimento
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Adicione os middlewares de autenticação e autorização
app.UseAuthentication(); // Deve vir antes de UseAuthorization
app.UseAuthorization();  // Deve vir depois de UseAuthentication

// --- Mapeamento dos Endpoints ---
app.MapUsuarioEndpoints();
app.MapClienteEndpoints();
app.MapQuartoEndpoints();
app.MapLocacaoEndpoints();
app.MapOcupacaoEndpoints();
app.MapResumoEndpoints();

app.Run();