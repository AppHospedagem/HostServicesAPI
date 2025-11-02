using AppHospedagemAPI.Data;
using AppHospedagemAPI.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System;

namespace AppHospedagemAPI.Endpoints
{
    public static class ResumoEndpoints
    {
        public static void MapResumoEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/dashboard")
                .WithTags("Dashboard")
                .RequireAuthorization("admin");

                

            group.MapGet("/resumo", async (AppDbContext db) =>
{
    var dataAtualUtc = DateTime.UtcNow.Date;
    var dataAmanhaUtc = dataAtualUtc.AddDays(1);

    // ✅ Status padronizados
    var statusReservado = "Reservado";
    var statusAtivo = "Ativo";
    var statusFinalizado = "Finalizado";
    var statusCancelado = "Cancelado";

    // 🏨 1. MÉTRICAS DE OCUPAÇÃO (EXISTENTES)
    var totalQuartos = await db.Quartos.CountAsync();
    
    var quartosOcupadosTotalmente = await db.Locacoes
        .Where(l => l.DataEntrada <= dataAtualUtc && 
                   l.DataSaida >= dataAtualUtc &&
                   l.Status == statusAtivo &&
                   l.TipoLocacao == "quarto")
        .Select(l => l.QuartoId)
        .Distinct()
        .CountAsync();

    var quartosParcialmenteOcupados = await db.Locacoes
        .Where(l => l.DataEntrada <= dataAtualUtc && 
                   l.DataSaida >= dataAtualUtc &&
                   l.Status == statusAtivo &&
                   l.TipoLocacao == "cama" &&
                   l.QuantidadeCamas > 0)
        .Select(l => l.QuartoId)
        .Distinct()
        .CountAsync();

    var quartosLivres = totalQuartos - (quartosOcupadosTotalmente + quartosParcialmenteOcupados);
    var taxaOcupacaoAtual = totalQuartos > 0 ? 
        (decimal)(quartosOcupadosTotalmente + quartosParcialmenteOcupados) / totalQuartos * 100 : 0;

    // 📅 2. RESERVAS E CHECK-IN/OUT (EXISTENTES + NOVAS)
    var reservasHoje = await db.Locacoes
        .CountAsync(l => l.DataEntrada.Date == dataAtualUtc && 
                        l.Status == statusReservado);

    // ➕ NOVO: Check-ins pendentes (reservas para hoje que ainda não fizeram check-in)
    var checkInsPendentes = await db.Locacoes
        .CountAsync(l => l.DataEntrada.Date == dataAtualUtc && 
                        l.Status == statusReservado);

    // ➕ NOVO: Check-outs pendentes (locações ativas que terminam hoje)
    var checkOutsPendentes = await db.Locacoes
        .CountAsync(l => l.DataSaida.Date == dataAtualUtc && 
                        l.Status == statusAtivo);

    // ➕ NOVO: No-shows (reservas de ontem que nunca fizeram check-in)
    var dataOntem = dataAtualUtc.AddDays(-1);
    var noShows = await db.Locacoes
        .CountAsync(l => l.DataEntrada.Date == dataOntem && 
                        l.Status == statusReservado);

    // 👥 3. CLIENTES (EXISTENTE)
    var clientesAtivosHoje = await db.Locacoes
        .Where(l => l.DataEntrada <= dataAtualUtc && 
                   l.DataSaida >= dataAtualUtc &&
                   l.Status == statusAtivo)
        .Select(l => l.ClienteId)
        .Distinct()
        .CountAsync();

    // 📈 4. PREVISÕES E ESTATÍSTICAS (NOVAS)
    // ➕ Previsão ocupação amanhã (baseado em reservas confirmadas)
    var reservasAmanha = await db.Locacoes
        .CountAsync(l => l.DataEntrada.Date == dataAmanhaUtc && 
                        (l.Status == statusReservado || l.Status == statusAtivo));
    
    var previsaoOcupacaoAmanha = totalQuartos > 0 ? 
        (decimal)reservasAmanha / totalQuartos * 100 : 0;

    // ➕ Quarto mais popular (mais reservas nos últimos 30 dias)
    var trintaDiasAtras = dataAtualUtc.AddDays(-30);
    var quartoMaisPopular = await db.Locacoes
        .Where(l => l.DataEntrada >= trintaDiasAtras)
        .GroupBy(l => l.Quarto.Numero)
        .OrderByDescending(g => g.Count())
        .Select(g => g.Key.ToString())
        .FirstOrDefaultAsync() ?? "N/A";

    // ➕ Tempo médio de estadia (em dias)
    var locacoesFinalizadas = await db.Locacoes
        .Where(l => l.Status == statusFinalizado && l.DataEntrada >= trintaDiasAtras)
        .ToListAsync();

    var tempoMedioEstadia = locacoesFinalizadas.Any() ?
        locacoesFinalizadas.Average(l => (l.DataSaida - l.DataEntrada).TotalDays) : 0;

    return Results.Ok(new DashboardResumoResponse
    {
        // 🏨 Ocupação
        TotalQuartos = totalQuartos,
        QuartosLivres = quartosLivres,
        QuartosOcupadosTotalmente = quartosOcupadosTotalmente,
        QuartosParcialmenteOcupados = quartosParcialmenteOcupados,
        TaxaOcupacaoAtual = Math.Round(taxaOcupacaoAtual, 1),
        
        // 📅 Reservas
        ReservasHoje = reservasHoje,
        CheckInsPendentes = checkInsPendentes,
        CheckOutsPendentes = checkOutsPendentes,
        NoShows = noShows,
        
        // 👥 Clientes
        ClientesAtivosHoje = clientesAtivosHoje,
        
        // 📈 Estatísticas
        PrevisaoOcupacaoAmanha = Math.Round(previsaoOcupacaoAmanha, 1),
        QuartoMaisPopular = quartoMaisPopular,
        TempoMedioEstadia = Math.Round((decimal)tempoMedioEstadia, 1)
    });
})
            .WithSummary("Obtém um resumo de estatísticas para o dashboard.")
            .WithDescription("Fornece informações sobre ocupação de quartos, reservas e clientes ativos para o dia atual.")
            .Produces<DashboardResumoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        }
    }
}