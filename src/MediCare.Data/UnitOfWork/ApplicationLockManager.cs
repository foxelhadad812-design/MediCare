using System.Collections.Concurrent;
using System.Data;
using MediCare.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MediCare.Data.UnitOfWork;

public sealed class NullAsyncDisposable : IAsyncDisposable, IDisposable
{
    public static readonly NullAsyncDisposable Instance = new();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Dispose() { }
}

public sealed class NullDatabaseTransaction : IDatabaseTransaction
{
    public static readonly NullDatabaseTransaction Instance = new();
    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Dispose() { }
}

public sealed class InMemoryDatabaseTransaction : IDatabaseTransaction
{
    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Dispose() { }
}

public sealed class EfDatabaseTransaction : IDatabaseTransaction
{
    private readonly IDbContextTransaction _transaction;

    public EfDatabaseTransaction(IDbContextTransaction transaction)
    {
        _transaction = transaction;
    }

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        return _transaction.CommitAsync(cancellationToken);
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        return _transaction.RollbackAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return _transaction.DisposeAsync();
    }

    public void Dispose()
    {
        _transaction.Dispose();
    }
}

public static class ApplicationLockManager
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public static async Task<IAsyncDisposable> AcquireLockAsync(
        ApplicationDbContext context,
        string lockKey,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        var acquired = await semaphore.WaitAsync(timeout, cancellationToken);
        if (!acquired)
        {
            throw new TimeoutException($"Timed out waiting to acquire application lock for key: {lockKey}");
        }

        try
        {
            if (context.Database.IsSqlServer() && context.Database.CurrentTransaction != null)
            {
                var timeoutMs = (int)Math.Max(0, timeout.TotalMilliseconds);
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"DECLARE @res int; EXEC @res = sp_getapplock @Resource = {lockKey}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {timeoutMs}; IF @res < 0 THROW 50001, 'Failed to acquire application lock in SQL Server', 1;",
                    cancellationToken);
            }
        }
        catch
        {
            // If SQL Server lock fails, release in-process semaphore and propagate
            semaphore.Release();
            throw;
        }

        return new Releaser(semaphore);
    }

    private sealed class Releaser : IAsyncDisposable, IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private int _disposed;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _semaphore.Release();
            }
        }
    }
}
