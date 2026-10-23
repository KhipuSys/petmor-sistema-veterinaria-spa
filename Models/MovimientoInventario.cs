using System;

namespace Petmor.Web.Models
{
    public class MovimientoInventario
    {
        public int Id { get; set; }
        public Guid? OperacionId { get; set; }
        
        public DateTime Fecha { get; set; }
        
        public int ProductoId { get; set; }
        public virtual Producto Producto { get; set; }

        /// <summary>Tipo: "ENTRADA", "SALIDA" o "AJUSTE"</summary>
        public string TipoMovimiento { get; set; }
        
        public int Cantidad { get; set; }
        
        public string Usuario { get; set; }

        public string Referencia { get; set; }

        /// <summary>Motivo del ajuste manual (merma, pérdida, corrección, etc.)</summary>
        public string Motivo { get; set; }
    }
}
