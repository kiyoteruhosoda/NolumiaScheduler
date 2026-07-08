using System.Text.Json;
using NolumiaScheduler.Domain.Aggregates;
using NolumiaScheduler.Domain.Repositories;
using NolumiaScheduler.Domain.ValueObjects;

namespace NolumiaScheduler.Infrastructure.Json.Repositories;

public class JsonBusinessCalendarRepository : IBusinessCalendarRepository
{
    private readonly string _directoryPath;

    public JsonBusinessCalendarRepository(string directoryPath)
    {
        _directoryPath = directoryPath;
        Directory.CreateDirectory(_directoryPath);
    }

    public BusinessCalendar? FindById(BusinessCalendarId id)
    {
        var path = GetFilePath(id);
        if (!File.Exists(path)) return null;

        var json = File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize(json, AppJsonContext.Default.BusinessCalendarDto);
        return dto?.ToDomain();
    }

    public IReadOnlyList<BusinessCalendar> FindAll()
    {
        var results = new List<BusinessCalendar>();
        foreach (var file in Directory.GetFiles(_directoryPath, "*.json"))
        {
            var json = File.ReadAllText(file);
            var dto = JsonSerializer.Deserialize(json, AppJsonContext.Default.BusinessCalendarDto);
            if (dto != null)
                results.Add(dto.ToDomain());
        }
        return results;
    }

    public void Save(BusinessCalendar calendar)
    {
        var dto = BusinessCalendarDto.FromDomain(calendar);
        var json = JsonSerializer.Serialize(dto, AppJsonContext.Default.BusinessCalendarDto);
        File.WriteAllText(GetFilePath(calendar.Id), json);
    }

    public void Delete(BusinessCalendarId id)
    {
        var path = GetFilePath(id);
        if (File.Exists(path))
            File.Delete(path);
    }

    private string GetFilePath(BusinessCalendarId id) => Path.Combine(_directoryPath, $"{id.Value}.json");
}

internal class BusinessCalendarDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string TimeZoneId { get; set; } = "";
    public List<string> Workdays { get; set; } = [];
    public List<HolidayDto> Holidays { get; set; } = [];
    public bool ShiftOnHolidaysOnly { get; set; }

    public BusinessCalendar ToDomain()
    {
        var workdays = Workdays.Select(w => Enum.Parse<Weekday>(w));
        var holidays = Holidays.Select(h =>
        {
            var d = DateOnly.Parse(h.Date);
            return new Holiday(new LocalDateValue(d.Year, d.Month, d.Day), h.Name);
        });

        return new BusinessCalendar(
            new BusinessCalendarId(Id),
            Name,
            new TimeZoneId(TimeZoneId),
            workdays,
            holidays,
            ShiftOnHolidaysOnly);
    }

    public static BusinessCalendarDto FromDomain(BusinessCalendar cal)
    {
        return new BusinessCalendarDto
        {
            Id = cal.Id.Value,
            Name = cal.Name,
            TimeZoneId = cal.TimeZoneId.Value,
            Workdays = [.. cal.Workdays.Select(w => w.ToString())],
            Holidays = [.. cal.Holidays.Select(h => new HolidayDto
            {
                Date = h.Date.ToString(),
                Name = h.Name
            })],
            ShiftOnHolidaysOnly = cal.ShiftOnHolidaysOnly
        };
    }
}

internal class HolidayDto
{
    public string Date { get; set; } = "";
    public string? Name { get; set; }
}
