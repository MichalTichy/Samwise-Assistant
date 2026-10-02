using MediatR;

namespace Samwise.Logic.Notifications.Usecases;

public sealed record SendDailyAutoExecutionSummaryUsecase(DateOnly ReportDate) : IRequest<int>;
