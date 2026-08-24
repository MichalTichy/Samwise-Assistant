using Marten;

namespace Shared.Infrastructure.Persistence.Marten.SessionFactory;

public interface IAsyncSessionFactory
{
    Task<IQuerySession> QuerySessionAsync(UnitOfWorks.UnitOfWork uow);
    Task<IDocumentSession> OpenSessionAsync(UnitOfWorks.UnitOfWork uow);
}
