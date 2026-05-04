namespace SampleRepo;

public class OrderService : IOrderRepository
{
    private readonly IOrderRepository _repo;
    public OrderService(IOrderRepository repo) { _repo = repo; }
    public void PlaceOrder(string orderId) => _repo.Save(orderId);
}
