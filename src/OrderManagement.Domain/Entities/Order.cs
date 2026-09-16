using OrderManagement.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace OrderManagement.Domain.Entities
{
    public class Order
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }
        public OrderStatus Status { get; set; } = OrderStatus.Pending;
        public string ShippingAddress { get; set; } = null!;
        public decimal TotalAmount { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        public List<OrderItem> Items { get; set; } = [];

        public int Version { get; set; }
    }
}
