namespace HocLieu.Domain;

public enum SystemRole { Teacher, Admin }

public enum UserStatus { Pending, Active, Suspended, Rejected }

public enum TeamRole { Member, Deputy, Lead }

public enum ContentScope { Public, Teachers, Team, Private }

public enum PublishMode { Hidden, Visible, Scheduled }

public enum ModerationStatus { Approved, PendingReview, Rejected }

public enum SectionContentKind { Document, Quiz, Both }

public enum ProcessingStatus { Pending, Processing, Ready, Failed, NotApplicable }

public enum QuestionType { Single, Multi, TrueFalse }

public enum ShowAnswers { Never, AfterSubmit, AfterClose }

public enum IdentityMode { Anonymous, Name, NameAndClass }

public enum MultiScoring { AllOrNothing, Partial }

public enum ScoreRounding { None, Quarter, Half, Integer }

public enum AttemptStatus { InProgress, Submitted, Expired }

public enum ReportStatus { Open, Resolved, Dismissed }

public enum AnnouncementAudience { Public, Teachers, Team }

public enum FavoriteItemType { Document, Quiz }
