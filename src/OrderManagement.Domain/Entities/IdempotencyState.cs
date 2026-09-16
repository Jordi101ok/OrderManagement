using System;
using System.Collections.Generic;
using System.Text;

namespace OrderManagement.Domain.Entities
{
    public enum IdempotencyState
    {
        InProgress = 0,
        Completed = 1
    }

    public class IdempotencyRecord
    {
        public Guid Id { get; set; }
        public string Key { get; set; } = null!;
        public string RequestHash { get; set; } = null!;
        public IdempotencyState State { get; set; }
        public Guid? OrderId { get; set; }
        public string? ResponseBody { get; set; }
        public int? StatusCode { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}
