using Hub.Core.Devices;
using Microsoft.EntityFrameworkCore;

namespace Hub.Data;

/// <summary>Hiện thực <see cref="IDeviceStore"/> bằng EF Core.</summary>
public sealed class EfDeviceStore(HubDbContext dbContext) : IDeviceStore
{
    public async Task<RegisteredDevice?> GetAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Devices
            .FirstOrDefaultAsync(device => device.Id == deviceId, cancellationToken);
    }

    public async Task<IReadOnlyList<RegisteredDevice>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Devices
            .AsNoTracking()
            .OrderBy(device => device.Hostname)
            .ToListAsync(cancellationToken);
    }

    public async Task<RegisteredDevice?> FindByHostnameAsync(
        string hostname,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Devices
            .FirstOrDefaultAsync(device => device.Hostname == hostname, cancellationToken);
    }

    public async Task AddAsync(RegisteredDevice device, CancellationToken cancellationToken = default)
    {
        dbContext.Devices.Add(device);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        RegisteredDevice device,
        CancellationToken cancellationToken = default)
    {
        if (dbContext.Entry(device).State == EntityState.Detached)
        {
            dbContext.Devices.Update(device);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var removed = await dbContext.Devices
            .Where(device => device.Id == deviceId)
            .ExecuteDeleteAsync(cancellationToken);

        return removed > 0;
    }

    public async Task RecordCommandAsync(
        DeviceCommandAudit audit,
        CancellationToken cancellationToken = default)
    {
        dbContext.DeviceCommands.Add(audit);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DeviceCommandAudit>> GetRecentCommandsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.DeviceCommands
            .AsNoTracking()
            .OrderByDescending(audit => audit.RequestedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
