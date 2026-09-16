using OrderManagement.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace OrderManagement.Domain
{
    public static class OrderStatusTransition
    {
        private static readonly Dictionary<OrderStatus, OrderStatus[]> Allowed = new()
        {
            [OrderStatus.Pending] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
            [OrderStatus.Confirmed] = [OrderStatus.Shipped, OrderStatus.Cancelled],
            [OrderStatus.Shipped] = [OrderStatus.Delivered],
            [OrderStatus.Delivered] = [],
            [OrderStatus.Cancelled] = []
        };

        public static bool CanTransition(OrderStatus from, OrderStatus to)
            => Allowed[from].Contains(to);

        public static bool IsTerminal(OrderStatus status)
            => Allowed[status].Length == 0;

        public static bool CanCancel(OrderStatus status)
            => status is OrderStatus.Pending or OrderStatus.Confirmed;
    }
}
