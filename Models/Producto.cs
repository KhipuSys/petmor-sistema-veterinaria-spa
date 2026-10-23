using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiniMarket.Web.Models
{
    public class Producto : IValidatableObject
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio")]
        public string Nombre { get; set; }

        public string Descripcion { get; set; }

        public string CodigoBarras { get; set; }

        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public List<CodigoProducto> Codigos { get; set; } = new();

        public string ImagenUrl { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Required(ErrorMessage = "El precio es obligatorio")]
        [Range(0, 100000000)]
        public decimal Precio { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Range(0, 100000000)]
        public decimal Costo { get; set; }

        // Columna anterior conservada únicamente para migrar instalaciones existentes.
        [Column("Stock"), Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public int StockLegado { get; set; }

        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public List<ExistenciaSucursal> Existencias { get; set; } = new();

        // EF carga automáticamente SOLO la existencia de la sede activa.
        [NotMapped]
        public int Stock
        {
            get => Existencias.SingleOrDefault()?.Cantidad ?? 0;
            set
            {
                var existencia = Existencias.SingleOrDefault();
                if (existencia == null) Existencias.Add(new ExistenciaSucursal { Cantidad = value });
                else existencia.Cantidad = value;
            }
        }

        // --- NUEVO CAMPO: Límite para la alerta ---
        [Display(Name = "Stock Mínimo")]
        [Range(0, int.MaxValue, ErrorMessage = "El stock mínimo no puede ser negativo.")]
        public int StockMinimo { get; set; } = 5; 

        public bool Estado { get; set; } = true;
        public bool ConCaducidad { get; set; }
        [DataType(DataType.Date)]
        public DateTime? FechaVencimiento { get; set; }
        [NotMapped] public bool Vencido => ConCaducidad && FechaVencimiento?.Date < HoraPeru.Hoy;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (ConCaducidad && !FechaVencimiento.HasValue)
                yield return new ValidationResult("Indique la fecha de vencimiento.", new[] { nameof(FechaVencimiento) });
        }
        public bool EsServicio { get; set; }
        public bool UsoInterno { get; set; }
        [MaxLength(20)] public string Unidad { get; set; } = "unidad";
        public AreaAtencion? AreaServicio { get; set; }
        public int DuracionMinutos { get; set; } = 30;

        public int CategoriaId { get; set; }
        public virtual Categoria Categoria { get; set; }
    }
}
