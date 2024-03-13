using System.ComponentModel.DataAnnotations;


    public class PeriodTableModel
    {
        [Key] public int Id { get; set; }
        public double Speed { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
    }
