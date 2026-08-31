namespace FixtureDataApp.Contracts;

/// <summary>본문에서 UserNo를 그대로 받는 요청. 인증 값과 대조하지 않으면 SHARD-005가 걸린다.</summary>
public sealed record CraftRequest(int UserNo, int ItemId);

public sealed record AttendanceRequest(int UserNo, string Day);

public sealed record MailSendRequest(int SenderNo, int TargetNo, string Title, int Cost);
