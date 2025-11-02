using AppHospedagemAPI.Models;
using AppHospedagemAPI.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using AppHospedagemAPI.DTOs;
using System.Security.Claims;

namespace AppHospedagemAPI.Endpoints
{


    public static class LocacaoEndpoints
    {
        public static void MapLocacaoEndpoints(this WebApplication app)
        {


            var group = app.MapGroup("/locacoes")
                .WithTags("Locações");

            // ➕ Criar nova locação (reserva)
            group.MapPost("/", async (
                [FromBody] LocacaoCreateRequest request,
                ClaimsPrincipal user,
                AppDbContext db) =>
            {
                // A validação das Data Annotations em LocacaoCreateRequest é automática.

                // Validação de existência de Cliente e Quarto
                var cliente = await db.Clientes.FindAsync(request.ClienteId);
                if (cliente == null) return Results.BadRequest("Cliente não encontrado.");

                var quarto = await db.Quartos.FindAsync(request.QuartoId);
                if (quarto == null) return Results.BadRequest("Quarto não encontrado.");

                var dataEntradaUtc = request.DataEntrada.ToUniversalTime();
                var dataSaidaUtc = request.DataSaida.ToUniversalTime();

                // Validação de regras de negócio para disponibilidade
                var statusValidos = new[] { "Reservado", "Ativo" };

                var conflitos = await db.Locacoes
                    .Where(l => l.QuartoId == request.QuartoId &&
                                statusValidos.Contains(l.Status) &&   // ✅ Só considera Reservado e Ativo
                                l.DataEntrada < dataSaidaUtc &&
                                l.DataSaida > dataEntradaUtc)
                    .ToListAsync();

                // 1. Verificar conflito para locação de quarto inteiro
                if (request.TipoLocacao == "quarto")
                {
                    if (conflitos.Any())
                    {
                        return Results.BadRequest("Quarto já reservado para o período selecionado.");
                    }
                    request.QuantidadeCamas = quarto.QuantidadeCamas;
                }
                // 2. Verificar disponibilidade de camas na locação por cama
                else if (request.TipoLocacao == "cama")
                {
                    var camasOcupadasConflito = conflitos
                        .Sum(c => c.TipoLocacao == "quarto" ? quarto.QuantidadeCamas : c.QuantidadeCamas);

                    if (camasOcupadasConflito + request.QuantidadeCamas > quarto.QuantidadeCamas)
                    {
                        return Results.BadRequest("Não há camas disponíveis suficientes para este quarto no período.");
                    }
                    if (request.QuantidadeCamas == null || request.QuantidadeCamas <= 0)
                    {
                        return Results.BadRequest("Quantidade de camas deve ser informada e maior que zero para locação por cama.");
                    }
                }
                else
                {
                    return Results.BadRequest("Tipo de locação inválido. Deve ser 'quarto' ou 'cama'.");
                }

                // Obter o ID do usuário logado
                var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var usuarioId))
                {
                    return Results.Unauthorized();
                }


                // Criar locação
                var locacao = new Locacao
                {
                    ClienteId = request.ClienteId,
                    QuartoId = request.QuartoId,
                    DataEntrada = dataEntradaUtc.Date, // Você pode manter o .Date, pois o Kind já é UTC
                    DataSaida = dataSaidaUtc.Date,
                    TipoLocacao = request.TipoLocacao,
                    QuantidadeCamas = request.QuantidadeCamas ?? 0,
                    Status = "Reservado",
                    CheckInRealizado = false,
                    CheckOutRealizado = false,
                    // PrecoTotal REMOVIDO
                    UsuarioId = usuarioId
                };

                db.Locacoes.Add(locacao);
                await db.SaveChangesAsync();

                // Retorna DTO de resposta completo
                return Results.Created($"/locacoes/{locacao.Id}", new LocacaoResponse
                {
                    Id = locacao.Id,
                    ClienteId = locacao.ClienteId,
                    ClienteNome = cliente.Nome,
                    QuartoId = locacao.QuartoId,
                    QuartoNumero = quarto.Numero,
                    DataEntrada = locacao.DataEntrada,
                    DataSaida = locacao.DataSaida,
                    TipoLocacao = locacao.TipoLocacao,
                    QuantidadeCamas = locacao.QuantidadeCamas,
                    Status = locacao.Status,
                    CheckInRealizado = locacao.CheckInRealizado,
                    CheckOutRealizado = locacao.CheckOutRealizado,
                    // PrecoTotal REMOVIDO
                    UsuarioResponsavelLogin = user.Identity?.Name
                });
            })
            .RequireAuthorization("admin")
            .WithSummary("Cria uma nova locação (reserva) para um quarto ou cama.")
            .Produces<LocacaoResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);


            // 🟢 Realizar check-in
            group.MapPost("/checkin/{id}", async (int id, AppDbContext db) =>
            {
                var locacao = await db.Locacoes.FindAsync(id);
                if (locacao == null)
                    return Results.NotFound("Locação não encontrada.");

                if (locacao.CheckInRealizado)
                    return Results.BadRequest("Check-in já foi realizado para esta locação.");

                if (locacao.Status != "Reservado")
                    return Results.BadRequest($"Não é possível realizar check-in para locação com status '{locacao.Status}'.");

                locacao.CheckInRealizado = true;
                locacao.Status = "Ativo";
                await db.SaveChangesAsync();

                return Results.Ok("Check-in realizado com sucesso.");
            })
            .RequireAuthorization("admin")
            .WithSummary("Realiza o check-in de uma locação.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);


            // 🔴 Realizar check-out COM DATA PERSONALIZADA
            group.MapPost("/checkout/{id}", async (int id, [FromBody] CheckoutRequest? request, AppDbContext db) =>
            {
                var locacao = await db.Locacoes.FindAsync(id);
                if (locacao == null)
                    return Results.NotFound("Locação não encontrada.");

                if (!locacao.CheckInRealizado)
                    return Results.BadRequest("Check-in ainda não foi realizado para esta locação.");

                if (locacao.CheckOutRealizado)
                    return Results.BadRequest("Check-out já foi realizado para esta locação.");

                if (locacao.Status != "Ativo")
                    return Results.BadRequest($"Não é possível realizar check-out para locação com status '{locacao.Status}'.");

                // ✅ DATA PERSONALIZADA DO CHECK-OUT
                var dataCheckout = request?.DataCheckout?.ToUniversalTime().Date ?? DateTime.UtcNow.Date;

                // Validação: data checkout não pode ser antes do check-in
                if (dataCheckout < locacao.DataEntrada.Date)
                    return Results.BadRequest("Data de check-out não pode ser anterior ao check-in.");

                // Validação: data checkout não pode ser no futuro (exceto para admin em casos especiais)
                if (dataCheckout > DateTime.UtcNow.Date)
                {
                    // Para permitir check-out futuro, você pode adicionar uma validação de permissão aqui
                    return Results.BadRequest("Data de check-out não pode ser no futuro.");
                }

                locacao.CheckOutRealizado = true;
                locacao.Status = "Finalizado";
                locacao.DataSaida = dataCheckout; // ✅ ATUALIZA A DATA REAL

                await db.SaveChangesAsync();

                return Results.Ok(new
                {
                    Message = "Check-out realizado com sucesso!",
                    DataCheckout = dataCheckout,
                    DataProgramada = locacao.DataEntrada // Para referência
                });
            })
            .RequireAuthorization("admin")
            .WithSummary("Realiza o check-out de uma locação com data personalizada.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);




            // 📋 Listar todas as locações com filtros e detalhes
            group.MapGet("/", async (
                [FromQuery] int? clienteId,
                [FromQuery] int? quartoId,
                [FromQuery] string? status,
                [FromQuery] DateTime? dataEntradaMin,
                [FromQuery] DateTime? dataSaidaMax,
                AppDbContext db) =>
            {
                var query = db.Locacoes
                    .Include(l => l.Cliente)
                    .Include(l => l.Quarto)
                    .Include(l => l.Usuario)
                    .AsQueryable();

                if (clienteId.HasValue) query = query.Where(l => l.ClienteId == clienteId.Value);
                if (quartoId.HasValue) query = query.Where(l => l.QuartoId == quartoId.Value);
                if (!string.IsNullOrEmpty(status)) query = query.Where(l => l.Status == status);
                if (dataEntradaMin.HasValue) query = query.Where(l => l.DataEntrada >= dataEntradaMin.Value.Date);
                if (dataSaidaMax.HasValue) query = query.Where(l => l.DataSaida <= dataSaidaMax.Value.Date);

                var locacoes = await query.OrderByDescending(l => l.DataEntrada).ToListAsync();

                // Mapeia para LocacaoResponse para formatar a saída
                return Results.Ok(locacoes.Select(l => new LocacaoResponse
                {
                    Id = l.Id,
                    ClienteId = l.ClienteId,
                    ClienteNome = l.Cliente?.Nome,
                    QuartoId = l.QuartoId,
                    QuartoNumero = l.Quarto?.Numero ?? 0,
                    DataEntrada = l.DataEntrada,
                    DataSaida = l.DataSaida,
                    TipoLocacao = l.TipoLocacao,
                    QuantidadeCamas = l.QuantidadeCamas,
                    Status = l.Status,
                    CheckInRealizado = l.CheckInRealizado,
                    CheckOutRealizado = l.CheckOutRealizado,
                    // PrecoTotal REMOVIDO
                    UsuarioResponsavelLogin = l.Usuario?.Login
                }));
            })
            .WithSummary("Lista todas as locações com filtros.")
            .Produces<IEnumerable<LocacaoResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);


            // 🔍 Buscar locação por ID com detalhes
            group.MapGet("/{id}", async (int id, AppDbContext db) =>
            {
                var locacao = await db.Locacoes
                    .Include(l => l.Cliente)
                    .Include(l => l.Quarto)
                    .Include(l => l.Usuario)
                    .FirstOrDefaultAsync(l => l.Id == id);

                return locacao is null
                    ? Results.NotFound("Locação não encontrada.")
                    : Results.Ok(new LocacaoResponse
                    {
                        Id = locacao.Id,
                        ClienteId = locacao.ClienteId,
                        ClienteNome = locacao.Cliente?.Nome,
                        QuartoId = locacao.QuartoId,
                        QuartoNumero = locacao.Quarto?.Numero ?? 0,
                        DataEntrada = locacao.DataEntrada,
                        DataSaida = locacao.DataSaida,
                        TipoLocacao = locacao.TipoLocacao,
                        QuantidadeCamas = locacao.QuantidadeCamas,
                        Status = locacao.Status,
                        CheckInRealizado = locacao.CheckInRealizado,
                        CheckOutRealizado = locacao.CheckOutRealizado,
                        // PrecoTotal REMOVIDO
                        UsuarioResponsavelLogin = locacao.Usuario?.Login
                    });
            })
            .WithSummary("Obtém detalhes de uma locação específica pelo ID.")
            .Produces<LocacaoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized);


            // ✏️ Atualizar Status da locação (Cancelar/Reativar) - Apenas Admin
            group.MapPut("/{id}/status/{newStatus}", async (int id, string newStatus, AppDbContext db) =>
            {
                var locacao = await db.Locacoes.FindAsync(id);
                if (locacao == null) return Results.NotFound("Locação não encontrada.");

                newStatus = newStatus.ToLower();

                switch (newStatus)
                {
                    case "cancelado":
                        if (locacao.CheckInRealizado || locacao.CheckOutRealizado)
                        {
                            return Results.BadRequest("Não é possível cancelar uma locação que já teve check-in ou check-out.");
                        }
                        break;
                    case "reservado":
                        if (locacao.Status != "cancelado")
                        {
                            return Results.BadRequest("Só é possível mudar o status para 'reservado' a partir de uma locação cancelada.");
                        }
                        locacao.CheckInRealizado = false;
                        locacao.CheckOutRealizado = false;
                        break;
                    default:
                        return Results.BadRequest("Status inválido. Status permitidos: 'Cancelado', 'Reservado'.");
                }

                locacao.Status = newStatus;
                await db.SaveChangesAsync();

                return Results.NoContent();
            })
            .RequireAuthorization("admin")
            .WithSummary("Atualiza o status de uma locação (e.g., para 'Cancelado' ou 'Reservado').")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

            // ✏️ Editar locação existente
group.MapPut("/{id}", async (
    int id,
    [FromBody] LocacaoUpdateRequest request,
    ClaimsPrincipal user,
    AppDbContext db) =>
{
    // 🔥 CORREÇÃO: Carregar a locação COM as propriedades de navegação
    var locacao = await db.Locacoes
        .Include(l => l.Quarto)  // 🔥 ADICIONAR ESTE INCLUDE
        .Include(l => l.Cliente) // 🔥 ADICIONAR PARA EVITAR OUTROS NULLs
        .FirstOrDefaultAsync(l => l.Id == id);
        
    if (locacao == null)
        return Results.NotFound("Locação não encontrada.");

    // ✅ VALIDAÇÃO: Não pode editar locações finalizadas ou canceladas
    if (locacao.Status == "Finalizado" || locacao.Status == "Cancelado")
    {
        return Results.BadRequest("Não é possível editar locações finalizadas ou canceladas.");
    }

    // ✅ VALIDAÇÃO: Se já fez check-in, só permite alterar data de saída
    if (locacao.CheckInRealizado)
    {
        // 🔥 SE JÁ FEZ CHECK-IN: Só permite alterar data de saída
        if (request.QuartoId.HasValue && request.QuartoId.Value != locacao.QuartoId ||
            request.TipoLocacao != null && request.TipoLocacao != locacao.TipoLocacao ||
            request.QuantidadeCamas.HasValue && request.QuantidadeCamas.Value != locacao.QuantidadeCamas ||
            request.DataEntrada.HasValue) // 🔥 ADICIONADO: Não pode alterar data entrada após check-in
        {
            return Results.BadRequest("Após o check-in, só é possível alterar a data de saída.");
        }
    }

    // 🔥 CORREÇÃO: quarto já está carregado, não precisa acessar locacao.Quarto
    var quarto = locacao.Quarto; // ✅ AGORA NÃO É MAIS NULL
    
    if (request.QuartoId.HasValue && request.QuartoId.Value != locacao.QuartoId)
    {
        quarto = await db.Quartos.FindAsync(request.QuartoId.Value);
        if (quarto == null) return Results.BadRequest("Quarto não encontrado.");
    }

    // 🔥 NOVO: VALIDAÇÃO DE DATA DE ENTRADA (apenas se não fez check-in)
    if (request.DataEntrada.HasValue && !locacao.CheckInRealizado)
    {
        var novaDataEntrada = request.DataEntrada.Value.ToUniversalTime().Date;

        // Validação: nova data de entrada não pode ser no passado
        if (novaDataEntrada < DateTime.UtcNow.Date)
            return Results.BadRequest("Nova data de entrada não pode ser no passado.");

        // Validação: nova data de entrada não pode ser após a data de saída
        var dataSaidaAtual = request.DataSaida.HasValue 
            ? request.DataSaida.Value.ToUniversalTime().Date 
            : locacao.DataSaida.Date;
            
        if (novaDataEntrada >= dataSaidaAtual)
            return Results.BadRequest("Nova data de entrada não pode ser igual ou posterior à data de saída.");

        locacao.DataEntrada = novaDataEntrada;
    }

    // ✅ ATUALIZA DATA DE SAÍDA (sempre permitido)
    if (request.DataSaida.HasValue)
    {
        var novaDataSaida = request.DataSaida.Value.ToUniversalTime().Date;

        // Validação: nova data de saída não pode ser antes da data de entrada
        var dataEntradaAtual = request.DataEntrada.HasValue 
            ? request.DataEntrada.Value.ToUniversalTime().Date 
            : locacao.DataEntrada.Date;
            
        if (novaDataSaida < dataEntradaAtual)
            return Results.BadRequest("Nova data de saída não pode ser anterior à data de entrada.");

        locacao.DataSaida = novaDataSaida;
    }

    // ✅ ATUALIZA OUTROS CAMPOS (apenas se não fez check-in)
    if (!locacao.CheckInRealizado)
    {
        if (request.QuartoId.HasValue)
            locacao.QuartoId = request.QuartoId.Value;

        if (!string.IsNullOrEmpty(request.TipoLocacao))
            locacao.TipoLocacao = request.TipoLocacao;

        if (request.QuantidadeCamas.HasValue)
            locacao.QuantidadeCamas = request.QuantidadeCamas.Value;
    }

    // ✅ VALIDAÇÃO DE DISPONIBILIDADE (apenas se mudou quarto ou datas)
    var mudouQuarto = request.QuartoId.HasValue && request.QuartoId.Value != locacao.QuartoId;
    var mudouDataEntrada = request.DataEntrada.HasValue && !locacao.CheckInRealizado;
    var mudouDataSaida = request.DataSaida.HasValue;

    if (mudouQuarto || mudouDataEntrada || mudouDataSaida)
    {
        var statusValidos = new[] { "Reservado", "Ativo" };
        
        // 🔥 CORREÇÃO: Usar as datas atualizadas para a verificação de conflito
        var dataEntradaParaVerificacao = mudouDataEntrada 
            ? request.DataEntrada.Value.ToUniversalTime().Date 
            : locacao.DataEntrada;
            
        var dataSaidaParaVerificacao = mudouDataSaida 
            ? request.DataSaida.Value.ToUniversalTime().Date 
            : locacao.DataSaida;

        var quartoIdParaVerificacao = mudouQuarto 
            ? request.QuartoId.Value 
            : locacao.QuartoId;

        var conflitos = await db.Locacoes
            .Where(l => l.QuartoId == quartoIdParaVerificacao &&
                       l.Id != locacao.Id && // Exclui a própria locação
                       statusValidos.Contains(l.Status) &&
                       l.DataEntrada < dataSaidaParaVerificacao &&
                       l.DataSaida > dataEntradaParaVerificacao)
            .ToListAsync();

        if (locacao.TipoLocacao == "quarto" && conflitos.Any())
        {
            return Results.BadRequest("Quarto já reservado para o período selecionado.");
        }
        else if (locacao.TipoLocacao == "cama")
        {
            var camasOcupadasConflito = conflitos
                .Sum(c => c.TipoLocacao == "quarto" ? quarto.QuantidadeCamas : c.QuantidadeCamas);

            if (camasOcupadasConflito + locacao.QuantidadeCamas > quarto.QuantidadeCamas)
            {
                return Results.BadRequest("Não há camas disponíveis suficientes para este quarto no período.");
            }
        }
    }

    await db.SaveChangesAsync();

    // 🔥 CORREÇÃO: Já carregamos os dados relacionados, não precisa recarregar
    return Results.Ok(new LocacaoResponse
    {
        Id = locacao.Id,
        ClienteId = locacao.ClienteId,
        ClienteNome = locacao.Cliente?.Nome,
        QuartoId = locacao.QuartoId,
        QuartoNumero = quarto?.Numero ?? 0, // 🔥 Usa a variável quarto que pode ter sido atualizada
        DataEntrada = locacao.DataEntrada,
        DataSaida = locacao.DataSaida,
        TipoLocacao = locacao.TipoLocacao,
        QuantidadeCamas = locacao.QuantidadeCamas,
        Status = locacao.Status,
        CheckInRealizado = locacao.CheckInRealizado,
        CheckOutRealizado = locacao.CheckOutRealizado,
        UsuarioResponsavelLogin = user.Identity?.Name // 🔥 Já temos o user do ClaimsPrincipal
    });
})
.RequireAuthorization("admin")
.WithSummary("Edita uma locação existente.")
.Produces<LocacaoResponse>(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status401Unauthorized)
.ProducesProblem(StatusCodes.Status403Forbidden);



            // ❌ Remover locação (Apenas Admin e se for "reservado" ou "cancelado")
            group.MapDelete("/{id}", async (int id, AppDbContext db) =>
            {
                var locacao = await db.Locacoes.FindAsync(id);
                if (locacao == null)
                    return Results.NotFound("Locação não encontrada.");

                if (locacao.Status == "Ativo" || locacao.Status == "Finalizado")
                {
                    return Results.BadRequest("Não é possível excluir locações ativas ou finalizadas.");
                }

                db.Locacoes.Remove(locacao);
                await db.SaveChangesAsync();
                return Results.NoContent();
            })
            .RequireAuthorization("admin")
            .WithSummary("Exclui uma locação existente. Somente locações não ativas ou finalizadas podem ser excluídas.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        }
    }


}



