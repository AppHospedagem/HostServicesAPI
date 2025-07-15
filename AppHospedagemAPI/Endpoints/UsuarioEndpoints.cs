using AppHospedagemAPI.Models;
using AppHospedagemAPI.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration; // Necessário para IConfiguration

namespace AppHospedagemAPI.Endpoints
{
    public static class UsuarioEndpoints
    {
        public static void MapUsuarioEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/usuarios")
                .WithTags("Usuários");

            // 👥 Listar usuários (apenas admin)
            group.MapGet("/", async (AppDbContext db) =>
            {
                var usuarios = await db.Usuarios
                    .Select(u => new { u.Id, u.Nome, u.Login, u.Role })
                    .ToListAsync();

                return Results.Ok(usuarios);
            })
            .RequireAuthorization("admin")
            .WithSummary("Listar todos os usuários");

            // 🔍 Obter usuário por ID
            group.MapGet("/{id}", async (int id, AppDbContext db) =>
            {
                var usuario = await db.Usuarios
                    .Select(u => new { u.Id, u.Nome, u.Login, u.Role })
                    .FirstOrDefaultAsync(u => u.Id == id);

                return usuario is null
                    ? Results.NotFound("Usuário não encontrado")
                    : Results.Ok(usuario);
            })
            // Geralmente, para este endpoint, você pode querer que o usuário veja apenas o PRÓPRIO perfil.
            // Isso exigiria uma Policy mais avançada no Program.cs e/ou verificação do ClaimTypes.NameIdentifier
            // com o 'id' da rota. Por enquanto, qualquer autenticado pode ver qualquer um.
            .RequireAuthorization()
            .WithSummary("Obter usuário específico");

            // ➕ Criar novo usuário (apenas admin)
            group.MapPost("/", async (
                [FromBody] UsuarioCreateRequest request,
                AppDbContext db) =>
            {
                // Validação de unicidade do login (já feita pelo índice único no DB, mas é bom ter aqui também)
                if (await db.Usuarios.AnyAsync(u => u.Login == request.Login))
                    return Results.BadRequest("Login já está em uso");

                var usuario = new Usuario
                {
                    Nome = request.Nome,
                    Login = request.Login,
                    Role = request.Role ?? "funcionario" // Define a role, padrão "funcionario" se não especificado
                };

                // Usa o método SetSenha do modelo Usuario para gerar hash e salt
                usuario.SetSenha(request.Senha);

                db.Usuarios.Add(usuario);
                await db.SaveChangesAsync();

                return Results.Created($"/usuarios/{usuario.Id}",
                    new { usuario.Id, usuario.Nome, usuario.Login, usuario.Role });
            })
            .RequireAuthorization("admin")
            .WithSummary("Criar novo usuário")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);


            // 🔑 Login (público)
            group.MapPost("/login", async (
                [FromBody] LoginRequest request,
                [FromServices] IConfiguration config, // Injeta a configuração
                AppDbContext db) =>
            {
                var usuario = await db.Usuarios
                    .FirstOrDefaultAsync(u => u.Login == request.Login);

                // Verifica se o usuário existe e se a senha está correta usando o método do modelo
                if (usuario == null || !usuario.VerificarSenha(request.Senha))
                    return Results.Unauthorized();

                // Gera token JWT
                var token = GenerateJwtToken(usuario, config);

                return Results.Ok(new
                {
                    usuario.Id,
                    usuario.Nome,
                    usuario.Login,
                    usuario.Role,
                    Token = token
                });
            })
            .AllowAnonymous()
            .WithSummary("Autenticar usuário")
            .WithDescription("Retorna token JWT para acesso");

            // ✏️ Atualizar usuário (pode ser o próprio usuário ou um admin)
            group.MapPut("/{id}", async (
                int id,
                [FromBody] UsuarioUpdateRequest request,
                AppDbContext db) =>
            {
                var usuario = await db.Usuarios.FindAsync(id);
                if (usuario == null)
                    return Results.NotFound();

                // Validação de unicidade do login, excluindo o próprio usuário
                if (await db.Usuarios.AnyAsync(u => u.Login == request.Login && u.Id != id))
                    return Results.BadRequest("Login já está em uso");

                usuario.Nome = request.Nome;
                usuario.Login = request.Login;
                
                // Se uma nova senha for fornecida, usa o método SetSenha
                if (!string.IsNullOrEmpty(request.Senha))
                {
                    usuario.SetSenha(request.Senha);
                }

                // A Role só pode ser alterada por um admin, ou não permitida a alteração via PUT aqui.
                // Se desejar que o próprio usuário não altere a role, remova a linha abaixo.
                // Se quiser que apenas admin altere, adicione uma verificação de role aqui.
                if (!string.IsNullOrEmpty(request.Role))
                {
                    usuario.Role = request.Role; 
                }

                await db.SaveChangesAsync();
                return Results.NoContent();
            })
            .RequireAuthorization()
            .WithSummary("Atualizar usuário");

            // ❌ Remover usuário (apenas admin)
            group.MapDelete("/{id}", async (int id, AppDbContext db) =>
            {
                var usuario = await db.Usuarios.FindAsync(id);
                if (usuario == null)
                    return Results.NotFound();

                db.Usuarios.Remove(usuario);
                await db.SaveChangesAsync();

                return Results.NoContent();
            })
            .RequireAuthorization("admin")
            .WithSummary("Remover usuário");
        }

        // Método auxiliar para geração de JWT (sem alterações, pois já estava correto)
        private static string GenerateJwtToken(Usuario usuario, IConfiguration config)
        {
            var jwtSettings = config.GetSection("JwtSettings");
            var key = Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
                    new Claim(ClaimTypes.Name, usuario.Login),
                    new Claim(ClaimTypes.Role, usuario.Role)
                }),
                Expires = DateTime.UtcNow.AddMinutes(Convert.ToDouble(jwtSettings["ExpiryInMinutes"])),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature)
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        // --- Modelos de Request (DTOs aninhados) ---

        public class UsuarioCreateRequest
        {
            [Required(ErrorMessage = "Nome é obrigatório")]
            [StringLength(100, MinimumLength = 3, ErrorMessage = "Nome deve ter entre 3 e 100 caracteres")]
            public string Nome { get; set; } = string.Empty; // Inicialize para evitar null

            [Required(ErrorMessage = "Login é obrigatório")]
            [StringLength(50, MinimumLength = 5, ErrorMessage = "Login deve ter entre 5 e 50 caracteres")]
            public string Login { get; set; } = string.Empty;

            [Required(ErrorMessage = "Senha é obrigatória")]
            [StringLength(50, MinimumLength = 6, ErrorMessage = "Senha deve ter no mínimo 6 caracteres")]
            public string Senha { get; set; } = string.Empty;

            [RegularExpression("^(admin|gerente|funcionario)$", ErrorMessage = "Role inválida. Use: admin, gerente ou funcionario")]
            public string? Role { get; set; } // Pode ser null para usar o padrão "funcionario" no model
        }

        public class UsuarioUpdateRequest
        {
            [Required(ErrorMessage = "Nome é obrigatório")]
            [StringLength(100, MinimumLength = 3, ErrorMessage = "Nome deve ter entre 3 e 100 caracteres")]
            public string Nome { get; set; } = string.Empty;

            [Required(ErrorMessage = "Login é obrigatório")]
            [StringLength(50, MinimumLength = 5, ErrorMessage = "Login deve ter entre 5 e 50 caracteres")]
            public string Login { get; set; } = string.Empty;

            [StringLength(50, MinimumLength = 6, ErrorMessage = "Senha deve ter no mínimo 6 caracteres se for alterada")]
            public string? Senha { get; set; } // Nullable, pois a senha pode não ser alterada

            [RegularExpression("^(admin|gerente|funcionario)$", ErrorMessage = "Role inválida. Use: admin, gerente ou funcionario")]
            public string? Role { get; set; } // Role também pode ser atualizada (se permitido pela lógica de negócio)
        }

        public class LoginRequest
        {
            [Required(ErrorMessage = "Login é obrigatório")]
            public string Login { get; set; } = string.Empty;

            [Required(ErrorMessage = "Senha é obrigatória")]
            public string Senha { get; set; } = string.Empty;
        }
    }
}