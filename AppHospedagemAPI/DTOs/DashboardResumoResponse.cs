namespace AppHospedagemAPI.DTOs;

public class DashboardResumoResponse
{
    // 🏨 Ocupação
    public int TotalQuartos { get; set; }
    public int QuartosLivres { get; set; }
    public int QuartosOcupadosTotalmente { get; set; }
    public int QuartosParcialmenteOcupados { get; set; }
    public decimal TaxaOcupacaoAtual { get; set; }
    
    // 📅 Reservas
    public int ReservasHoje { get; set; }
    public int CheckInsPendentes { get; set; }
    public int CheckOutsPendentes { get; set; }
    public int NoShows { get; set; }
    
    // 👥 Clientes
    public int ClientesAtivosHoje { get; set; }
    
    // 📈 Estatísticas
    public decimal PrevisaoOcupacaoAmanha { get; set; }
    public string QuartoMaisPopular { get; set; }
    public decimal TempoMedioEstadia { get; set; }
}