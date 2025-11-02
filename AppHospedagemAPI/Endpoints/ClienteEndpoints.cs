using AppHospedagemAPI.Models;
using AppHospedagemAPI.DTOs;
using AppHospedagemAPI.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations; // Ainda necessário para ValidationResult se quiser usar em algum lugar, mas menos.
using AppHospedagemAPI.DTOs; // Adicione este using para seus novos DTOs

namespace AppHospedagemAPI.Endpoints
{
    public static class ClienteEndpoints
    {
        public static void MapClienteEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/clientes")
                .WithTags("Clientes")
                .RequireAuthorization(); // Adicionando autorização padrão: qualquer usuário autenticado

            // ➕ Criar novo cliente
            group.MapPost("/", async (
                [FromBody] ClienteCreateRequest request, // Agora usando ClienteCreateRequest DTO
                AppDbContext db) =>
            {
                // A validação das Data Annotations (e.g., [Required], [StringLength], [RegularExpression])
                // no ClienteCreateRequest é feita AUTOMATICAMENTE pelo ASP.NET Core antes deste código ser executado.
                // Se a validação falhar, um 400 Bad Request é retornado automaticamente.

                // Limpa documento e telefone antes de verificar/armazenar (remove caracteres não-numéricos)
                request.Documento = new string(request.Documento.Where(char.IsDigit).ToArray());
                request.Telefone = new string(request.Telefone.Where(char.IsDigit).ToArray());

                // Verifica se documento já existe no banco (validação de unicidade de negócio)
                if (await db.Clientes.AnyAsync(c => c.Documento == request.Documento))
                {
                    return Results.BadRequest("Documento já cadastrado.");
                }

                var cliente = new Cliente
                {
                    Nome = request.Nome,
                    Documento = request.Documento, // Já limpo
                    Telefone = request.Telefone    // Já limpo
                };

                db.Clientes.Add(cliente);
                await db.SaveChangesAsync();

                // Retorna ClienteResponse para padronizar a saída formatada
                return Results.Created($"/clientes/{cliente.Id}", new ClienteResponse
                {
                    Id = cliente.Id,
                    Nome = cliente.Nome,
                    Documento = FormatDocumento(cliente.Documento),
                    Telefone = FormatTelefone(cliente.Telefone)
                });
            })
            .RequireAuthorization("admin") // Exemplo: se apenas admins podem criar clientes
            .WithSummary("Cria um novo cliente")
            .WithDescription("Cadastra cliente com validação de documento (CPF/CNPJ) e telefone.")
            .Produces<ClienteResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);


            // 📋 Listar clientes (com formatação)
            group.MapGet("/", async (AppDbContext db) =>
            {
                var clientes = await db.Clientes.ToListAsync();
                return Results.Ok(clientes.Select(c => new ClienteResponse // Usando ClienteResponse
                {
                    Id = c.Id,
                    Nome = c.Nome,
                    Documento = FormatDocumento(c.Documento),
                    Telefone = FormatTelefone(c.Telefone)
                }));
            })
            .WithSummary("Lista todos os clientes")
            .Produces<IEnumerable<ClienteResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);


            // 🔍 Obter cliente por ID
            group.MapGet("/{id}", async (int id, AppDbContext db) =>
            {
                var cliente = await db.Clientes.FindAsync(id);
                return cliente is null
                    ? Results.NotFound("Cliente não encontrado.")
                    : Results.Ok(new ClienteResponse // Usando ClienteResponse
                    {
                        Id = cliente.Id,
                        Nome = cliente.Nome,
                        Documento = FormatDocumento(cliente.Documento),
                        Telefone = FormatTelefone(cliente.Telefone)
                    });
            })
            .WithSummary("Obtém um cliente específico pelo ID")
            .Produces<ClienteResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized);


            // 🔎 Buscar por documento (sem formatação na entrada, com formatação na saída)
            group.MapGet("/por-documento/{documento}", async (string documento, AppDbContext db) =>
            {
                var docLimpo = new string(documento.Where(char.IsDigit).ToArray()); // Limpa o documento para a busca
                var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Documento == docLimpo);

                return cliente is null
                    ? Results.NotFound("Cliente não encontrado com o documento fornecido.")
                    : Results.Ok(new ClienteResponse // Usando ClienteResponse
                    {
                        Id = cliente.Id,
                        Nome = cliente.Nome,
                        Documento = FormatDocumento(cliente.Documento),
                        Telefone = FormatTelefone(cliente.Telefone)
                    });
            })
            .WithSummary("Busca um cliente pelo documento (CPF/CNPJ)")
            .Produces<ClienteResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized);


            // ✏️ Atualizar cliente
            group.MapPut("/{id}", async (
                int id,
                [FromBody] ClienteUpdateRequest request, // Agora usando ClienteUpdateRequest DTO
                AppDbContext db) =>
            {
                // Validação automática das Data Annotations no ClienteUpdateRequest
                var cliente = await db.Clientes.FindAsync(id);
                if (cliente is null) return Results.NotFound("Cliente não encontrado para atualização.");

                // Note: Documento não é atualizado via PUT neste exemplo, pois é uma chave de identificação.

                cliente.Nome = request.Nome;
                cliente.Telefone = new string(request.Telefone.Where(char.IsDigit).ToArray()); // Limpa o telefone antes de salvar

                await db.SaveChangesAsync();
                return Results.NoContent();
            })
            .WithSummary("Atualiza os dados de um cliente existente")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized);


            // ❌ Excluir cliente
            group.MapDelete("/{id}", async (int id, AppDbContext db) =>
            {
                // 1. Primeiro verifica se o cliente existe
                var cliente = await db.Clientes.FindAsync(id);
                if (cliente is null)
                    return Results.NotFound("Cliente não encontrado para exclusão.");

                // 2. Consulta DIRETA para verificar reservas ativas - MÉTODO MAIS CONFIÁVEL
                var temReservasAtivas = await db.Locacoes
                    .AnyAsync(l => l.ClienteId == id &&
                                  (l.Status == "Reservado" || l.Status == "Ativo"));

                Console.WriteLine($"Cliente {id} - Tem reservas ativas: {temReservasAtivas}"); // Debug

                if (temReservasAtivas)
                {
                    return Results.BadRequest(new
                    {
                        message = "Não é possível excluir o cliente pois ele possui reservas ativas ou pendentes.",
                        errorCode = "CLIENTE_COM_RESERVAS_ATIVAS"
                    });
                }

                // 3. Se não tem reservas ativas, exclui o cliente
                db.Clientes.Remove(cliente);
                await db.SaveChangesAsync();

                return Results.NoContent();
            })
            .WithSummary("Exclui um cliente existente")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);



        }

        #region Métodos Auxiliares
        private static string FormatDocumento(string documento)
        {
            // Pequeno ajuste para garantir que mesmo documentos inválidos não quebrem a formatação
            var docLimpo = new string(documento.Where(char.IsDigit).ToArray());
            return docLimpo.Length switch
            {
                11 => Convert.ToUInt64(docLimpo).ToString(@"000\.000\.000\-00"),
                14 => Convert.ToUInt64(docLimpo).ToString(@"00\.000\.000\/0000\-00"),
                _ => documento // Retorna o original se não for 11 nem 14 dígitos (apesar da validação na entrada)
            };
        }

        private static string FormatTelefone(string telefone)
        {
            if (string.IsNullOrEmpty(telefone)) return string.Empty;
            var nums = new string(telefone.Where(char.IsDigit).ToArray());
            return nums.Length == 11
                ? long.Parse(nums).ToString(@"(00) 00000\-0000")
                : telefone;
        }
        #endregion
    }
}