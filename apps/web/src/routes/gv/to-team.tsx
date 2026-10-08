import { useState } from "react";
import { Navigate, useParams, useSearchParams } from "react-router";
import dayjs from "dayjs";
import {
  Check,
  CheckCheck,
  Copy,
  Loader2,
  Megaphone,
  Send,
  Undo2,
  Users,
  X,
} from "lucide-react";
import { useMe } from "@/features/auth/api";
import { apiErrorTitle } from "@/features/auth/api";
import { TZ } from "@/lib/date";
import { formatDateTime } from "@/lib/date";
import {
  invitationResultInfo,
  invitationStatusVariant,
  parseEmails,
  teamRoleLabel,
  teamRoleVariant,
} from "@/features/teams/labels";
import {
  useApproveJoinRequest,
  useBulkApproveJoinRequests,
  useCreateInvitations,
  useInvitations,
  useJoinRequests,
  usePostTeamAnnouncement,
  useRejectJoinRequest,
  useRemoveMember,
  useRevokeInvitation,
  useResendInvitation,
  useSetMemberRole,
  useTeam,
  useTeamAnnouncements,
  useTeamMembers,
  useTeamStats,
} from "@/features/teams/api";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { EmptyState } from "@/components/common/EmptyState";

type TabKey = "thanh-vien" | "cho-duyet" | "loi-moi" | "thong-bao" | "thong-ke";

/**
 * §5.2 /gv/to/:teamId — không gian tổ.
 * Tab Chờ duyệt / Lời mời / đăng Thông báo chỉ Lead|Deputy (Admin xem được tất cả).
 */
export function ToTeamPage() {
  const params = useParams();
  const teamId = Number(params.teamId);
  const [searchParams, setSearchParams] = useSearchParams();
  const tab = (searchParams.get("tab") as TabKey | null) ?? "thanh-vien";

  const { data: me } = useMe();
  const {
    data: team,
    isPending: teamLoading,
    isError: teamError,
  } = useTeam(teamId);

  if (!me) return <Navigate to="/dang-nhap" replace />;
  const myRole = me.teams.find((t) => t.id === teamId)?.role ?? null;
  const isMember = myRole != null;
  const isAdmin = me.systemRole === "Admin" || team?.isAdmin === true;
  const canManage = myRole === "Lead" || myRole === "Deputy" || isAdmin;
  const canOwn = myRole === "Lead" || me.systemRole === "Admin";

  if (teamLoading)
    return (
      <div className="flex justify-center py-16">
        <Loader2 className="size-6 animate-spin text-violet" aria-hidden />
      </div>
    );
  if (teamError || !team)
    return (
      <EmptyState
        icon={Users}
        title="Không mở được không gian tổ"
        description="Bạn không phải thành viên của tổ này hoặc tổ không tồn tại."
      />
    );
  if (!isMember && !isAdmin) return <Navigate to="/gv/to" replace />;

  const tabs: { key: TabKey; label: string; manageOnly?: boolean }[] = [
    { key: "thanh-vien", label: "Thành viên" },
    { key: "cho-duyet", label: "Chờ duyệt", manageOnly: true },
    { key: "loi-moi", label: "Lời mời", manageOnly: true },
    { key: "thong-bao", label: "Thông báo tổ" },
    { key: "thong-ke", label: "Thống kê" },
  ];

  return (
    <div>
      <div className="flex flex-wrap items-center gap-2">
        <h1 className="text-xl font-semibold">{team.name}</h1>
        {team.gradeName && <Badge variant="outline">{team.gradeName}</Badge>}
        {myRole && (
          <Badge variant={teamRoleVariant(myRole)}>
            {teamRoleLabel(myRole)}
          </Badge>
        )}
        {isAdmin && <Badge variant="outline">Quyền quản trị</Badge>}
      </div>
      {team.description && (
        <p className="mt-1 max-w-2xl text-sm text-muted">{team.description}</p>
      )}

      <div
        role="tablist"
        className="mt-5 flex flex-wrap gap-1 border-b border-grid"
      >
        {tabs
          .filter((t) => !t.manageOnly || canManage)
          .map((t) => (
            <button
              key={t.key}
              role="tab"
              type="button"
              aria-selected={tab === t.key}
              onClick={() =>
                setSearchParams((prev) => {
                  const next = new URLSearchParams(prev);
                  next.set("tab", t.key);
                  return next;
                })
              }
              className={
                "relative -mb-px rounded-t-btn px-3 py-2 text-sm transition " +
                (tab === t.key
                  ? "border border-b-0 border-grid bg-white font-medium text-violet"
                  : "text-muted hover:text-ink")
              }
            >
              {t.label}
            </button>
          ))}
      </div>

      <div className="mt-5">
        {tab === "thanh-vien" && (
          <MembersTab teamId={teamId} canOwn={canOwn} myRoleId={me.id} />
        )}
        {tab === "cho-duyet" && canManage && (
          <JoinRequestsTab teamId={teamId} />
        )}
        {tab === "loi-moi" && canManage && <InvitationsTab teamId={teamId} />}
        {tab === "thong-bao" && (
          <AnnouncementsTab teamId={teamId} canManage={canManage} />
        )}
        {tab === "thong-ke" && <StatsTab teamId={teamId} />}
      </div>
    </div>
  );
}

// ===== Thành viên =====

function MembersTab({
  teamId,
  canOwn,
  myRoleId,
}: {
  teamId: number;
  canOwn: boolean;
  myRoleId: number;
}) {
  const { data: members, isPending } = useTeamMembers(teamId);
  const setRole = useSetMemberRole(teamId);
  const remove = useRemoveMember(teamId);

  if (isPending) return <TabLoading />;
  if (!members || members.length === 0)
    return (
      <EmptyState
        icon={Users}
        title="Chưa có thành viên"
        description="Mời đồng nghiệp qua tab Lời mời."
      />
    );

  return (
    <div className="overflow-x-auto rounded-card border border-grid bg-white">
      <table className="w-full min-w-[640px] text-sm">
        <thead>
          <tr className="border-b border-grid text-left text-muted">
            <th className="px-4 py-2.5 font-medium">Họ tên</th>
            <th className="px-4 py-2.5 font-medium">Email</th>
            <th className="px-4 py-2.5 font-medium">Vai trò</th>
            <th className="px-4 py-2.5 font-medium">Nội dung</th>
            <th className="px-4 py-2.5 font-medium">Vào tổ lúc</th>
            {canOwn && <th className="px-4 py-2.5 font-medium">Thao tác</th>}
          </tr>
        </thead>
        <tbody>
          {members.map((m) => (
            <tr
              key={m.userId}
              className="border-b border-grid/60 last:border-0"
            >
              <td className="px-4 py-2.5">
                <span className="flex items-center gap-2">
                  <Avatar src={m.avatarUrl} name={m.fullName} />
                  <span className="font-medium text-ink">{m.fullName}</span>
                  {m.userId === myRoleId && (
                    <span className="text-xs text-muted">(bạn)</span>
                  )}
                </span>
              </td>
              <td className="px-4 py-2.5 text-muted">{m.email}</td>
              <td className="px-4 py-2.5">
                <Badge variant={teamRoleVariant(m.role)}>
                  {teamRoleLabel(m.role)}
                </Badge>
              </td>
              <td className="px-4 py-2.5 tabular-nums">{m.contentCount}</td>
              <td className="px-4 py-2.5 text-muted">
                {formatDateTime(m.joinedAt)}
              </td>
              {canOwn && (
                <td className="px-4 py-2.5">
                  {m.role === "Lead" ? (
                    <span className="text-xs text-muted">—</span>
                  ) : (
                    <span className="flex flex-wrap gap-1.5">
                      <Button
                        size="sm"
                        variant="secondary"
                        disabled={setRole.isPending}
                        onClick={() =>
                          setRole.mutate({
                            userId: m.userId,
                            role: m.role === "Deputy" ? "Member" : "Deputy",
                          })
                        }
                      >
                        {m.role === "Deputy" ? "Bỏ tổ phó" : "Đặt tổ phó"}
                      </Button>
                      <Button
                        size="sm"
                        variant="ghost"
                        className="text-wrong hover:text-wrong"
                        disabled={remove.isPending}
                        onClick={() => {
                          if (
                            window.confirm(
                              `Gỡ ${m.fullName} khỏi tổ? Nội dung của người này vẫn thuộc quản lý của tổ.`,
                            )
                          )
                            remove.mutate(m.userId);
                        }}
                      >
                        Gỡ khỏi tổ
                      </Button>
                    </span>
                  )}
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// ===== Chờ duyệt =====

function JoinRequestsTab({ teamId }: { teamId: number }) {
  const { data: requests, isPending } = useJoinRequests(teamId);
  const approve = useApproveJoinRequest(teamId);
  const reject = useRejectJoinRequest(teamId);
  const bulkApprove = useBulkApproveJoinRequests(teamId);
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [rejectingId, setRejectingId] = useState<number | null>(null);
  const [reason, setReason] = useState("");
  const [notice, setNotice] = useState<string | null>(null);

  if (isPending) return <TabLoading />;
  if (!requests || requests.length === 0)
    return (
      <EmptyState
        icon={CheckCheck}
        title="Không có yêu cầu nào đang chờ"
        description="Khi giáo viên gửi yêu cầu tham gia tổ, họ sẽ xuất hiện ở đây."
      />
    );

  const allSelected = selected.size === requests.length;

  function toggle(userId: number) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(userId)) next.delete(userId);
      else next.add(userId);
      return next;
    });
  }

  function onBulk() {
    const ids = [...selected];
    bulkApprove.mutate(ids, {
      onSuccess: (results) => {
        setSelected(new Set());
        setNotice(
          `Đã duyệt ${results.filter((r) => r.ok).length}/${results.length} yêu cầu.`,
        );
      },
      onError: (e) => setNotice(apiErrorTitle(e, "Không duyệt được")),
    });
  }

  return (
    <div>
      {notice && <Notice text={notice} onClear={() => setNotice(null)} />}
      <div className="mb-3 flex items-center gap-2">
        <Button size="sm" disabled={selected.size === 0} onClick={onBulk}>
          <CheckCheck className="size-4" aria-hidden />
          Duyệt {selected.size > 0 ? `${selected.size} đã chọn` : ""}
        </Button>
        <span className="text-xs text-muted">
          {requests.length} yêu cầu đang chờ
        </span>
      </div>
      <div className="overflow-x-auto rounded-card border border-grid bg-white">
        <table className="w-full min-w-[720px] text-sm">
          <thead>
            <tr className="border-b border-grid text-left text-muted">
              <th className="w-10 px-4 py-2.5">
                <input
                  type="checkbox"
                  aria-label="Chọn tất cả"
                  checked={allSelected}
                  onChange={() =>
                    setSelected(
                      allSelected
                        ? new Set()
                        : new Set(requests.map((r) => r.userId)),
                    )
                  }
                />
              </th>
              <th className="px-4 py-2.5 font-medium">Họ tên</th>
              <th className="px-4 py-2.5 font-medium">Email</th>
              <th className="px-4 py-2.5 font-medium">SĐT</th>
              <th className="px-4 py-2.5 font-medium">Gửi lúc</th>
              <th className="px-4 py-2.5 font-medium">Thao tác</th>
            </tr>
          </thead>
          <tbody>
            {requests.map((r) => (
              <tr
                key={r.userId}
                className="border-b border-grid/60 last:border-0 align-top"
              >
                <td className="px-4 py-2.5">
                  <input
                    type="checkbox"
                    aria-label={`Chọn ${r.fullName}`}
                    checked={selected.has(r.userId)}
                    onChange={() => toggle(r.userId)}
                  />
                </td>
                <td className="px-4 py-2.5">
                  <span className="flex items-center gap-2">
                    <Avatar src={r.avatarUrl} name={r.fullName} />
                    <span className="font-medium text-ink">{r.fullName}</span>
                  </span>
                </td>
                <td className="px-4 py-2.5 text-muted">{r.email}</td>
                <td className="px-4 py-2.5 text-muted">{r.phone ?? "—"}</td>
                <td className="px-4 py-2.5 text-muted">
                  {formatDateTime(r.requestedAt)}
                </td>
                <td className="px-4 py-2.5">
                  {rejectingId === r.userId ? (
                    <span className="flex flex-wrap items-center gap-1.5">
                      <Input
                        className="h-8 w-48 text-xs"
                        placeholder="Lý do (tùy chọn)"
                        value={reason}
                        onChange={(e) => setReason(e.target.value)}
                      />
                      <Button
                        size="sm"
                        variant="destructive"
                        disabled={reject.isPending}
                        onClick={() => {
                          reject.mutate(
                            { userId: r.userId, reason: reason || undefined },
                            {
                              onSuccess: () => {
                                setRejectingId(null);
                                setReason("");
                                setNotice(`Đã từ chối ${r.fullName}.`);
                              },
                              onError: (e) =>
                                setNotice(
                                  apiErrorTitle(e, "Không từ chối được"),
                                ),
                            },
                          );
                        }}
                      >
                        Xác nhận
                      </Button>
                      <Button
                        size="sm"
                        variant="ghost"
                        onClick={() => {
                          setRejectingId(null);
                          setReason("");
                        }}
                      >
                        <X className="size-4" aria-hidden />
                      </Button>
                    </span>
                  ) : (
                    <span className="flex gap-1.5">
                      <Button
                        size="sm"
                        disabled={approve.isPending}
                        onClick={() =>
                          approve.mutate(r.userId, {
                            onSuccess: (res) =>
                              setNotice(
                                res.ok
                                  ? `Đã duyệt ${res.fullName}.`
                                  : (res.message ?? "Không duyệt được."),
                              ),
                            onError: (e) =>
                              setNotice(apiErrorTitle(e, "Không duyệt được")),
                          })
                        }
                      >
                        <Check className="size-4" aria-hidden /> Duyệt
                      </Button>
                      <Button
                        size="sm"
                        variant="outline"
                        onClick={() => {
                          setRejectingId(r.userId);
                          setReason("");
                        }}
                      >
                        Từ chối
                      </Button>
                    </span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ===== Lời mời =====

function InvitationsTab({ teamId }: { teamId: number }) {
  const { data: invites, isPending } = useInvitations(teamId);
  const create = useCreateInvitations(teamId);
  const resend = useResendInvitation(teamId);
  const revoke = useRevokeInvitation(teamId);
  const [emailsText, setEmailsText] = useState("");
  const [message, setMessage] = useState("");
  const [results, setResults] = useState<
    {
      email: string;
      label: string;
      tone: "ok" | "info" | "warn";
      link: string | null;
    }[]
  >([]);
  const [error, setError] = useState<string | null>(null);

  const validEmails = parseEmails(emailsText);

  function onInvite() {
    setError(null);
    create.mutate(
      { emails: validEmails, message: message || null },
      {
        onSuccess: (rs) => {
          setResults(
            rs.map((r) => {
              const info = invitationResultInfo(r);
              return { email: r.email, ...info, link: r.link };
            }),
          );
          setEmailsText("");
          setMessage("");
        },
        onError: (e) => setError(apiErrorTitle(e, "Không gửi được lời mời")),
      },
    );
  }

  const pendingLinks = results.filter((r) => r.link).map((r) => r.link!);

  return (
    <div className="space-y-5">
      <div className="rounded-card border border-grid bg-white p-4">
        <h2 className="font-medium">Mời giáo viên vào tổ</h2>
        <p className="mt-1 text-sm text-muted">
          Mỗi email một dòng (hoặc phân tách bằng dấu phẩy). Người chưa có tài
          khoản sẽ nhận link /moi — đăng nhập Google đúng email đó là vào tổ.
        </p>
        <div className="mt-3 grid gap-3 md:grid-cols-[1fr_240px]">
          <label className="block">
            <span className="mb-1 block text-sm font-medium text-ink">
              Email ({validEmails.length} hợp lệ)
            </span>
            <textarea
              value={emailsText}
              onChange={(e) => setEmailsText(e.target.value)}
              rows={4}
              placeholder={"nvanh@truong.edu.vn\nltbich@gmail.com"}
              className="w-full rounded-btn border border-grid bg-white p-3 text-sm outline-none transition focus:border-violet focus:ring-2 focus:ring-violet/20"
            />
          </label>
          <label className="block">
            <span className="mb-1 block text-sm font-medium text-ink">
              Lời nhắn (tùy chọn)
            </span>
            <Input
              value={message}
              onChange={(e) => setMessage(e.target.value)}
              placeholder="Chào thầy/cô, mời vào tổ…"
              maxLength={300}
            />
          </label>
        </div>
        {error && <p className="mt-2 text-sm text-wrong">{error}</p>}
        <div className="mt-3 flex flex-wrap items-center gap-2">
          <Button
            disabled={validEmails.length === 0 || create.isPending}
            onClick={onInvite}
          >
            {create.isPending ? (
              <Loader2 className="size-4 animate-spin" aria-hidden />
            ) : (
              <Send className="size-4" aria-hidden />
            )}
            Gửi lời mời
          </Button>
          {pendingLinks.length > 0 && (
            <>
              <Button
                variant="outline"
                onClick={() =>
                  navigator.clipboard.writeText(pendingLinks.join("\n"))
                }
              >
                <Copy className="size-4" aria-hidden />
                Sao chép {pendingLinks.length} link
              </Button>
              <span className="text-xs text-muted">
                (dán vào Zalo khi chưa cấu hình email)
              </span>
            </>
          )}
        </div>
        {results.length > 0 && (
          <ul className="mt-3 space-y-1">
            {results.map((r, i) => (
              <li
                key={i}
                className={
                  "flex items-center gap-2 rounded-btn px-3 py-1.5 text-sm " +
                  (r.tone === "ok"
                    ? "bg-correct/10 text-correct"
                    : r.tone === "warn"
                      ? "bg-warn/10 text-warn"
                      : "bg-violet/10 text-violet")
                }
              >
                <span className="font-medium">{r.email}</span>
                <span className="text-xs">— {r.label}</span>
                {r.link && (
                  <button
                    type="button"
                    className="text-xs underline hover:text-ink"
                    onClick={() => navigator.clipboard.writeText(r.link!)}
                  >
                    sao chép link
                  </button>
                )}
              </li>
            ))}
          </ul>
        )}
      </div>

      <div>
        <h2 className="mb-2 font-medium">Lời mời đã gửi</h2>
        {isPending ? (
          <TabLoading />
        ) : !invites || invites.length === 0 ? (
          <EmptyState
            icon={Send}
            title="Chưa có lời mời nào"
            description="Lời mời và trạng thái của chúng sẽ hiện ở đây."
          />
        ) : (
          <div className="overflow-x-auto rounded-card border border-grid bg-white">
            <table className="w-full min-w-[720px] text-sm">
              <thead>
                <tr className="border-b border-grid text-left text-muted">
                  <th className="px-4 py-2.5 font-medium">Email</th>
                  <th className="px-4 py-2.5 font-medium">Trạng thái</th>
                  <th className="px-4 py-2.5 font-medium">Vai trò</th>
                  <th className="px-4 py-2.5 font-medium">Người mời</th>
                  <th className="px-4 py-2.5 font-medium">Hết hạn</th>
                  <th className="px-4 py-2.5 font-medium">Thao tác</th>
                </tr>
              </thead>
              <tbody>
                {invites.map((inv) => (
                  <tr
                    key={inv.id}
                    className="border-b border-grid/60 last:border-0"
                  >
                    <td className="px-4 py-2.5">
                      <span className="block font-medium text-ink">
                        {inv.email}
                      </span>
                      {inv.acceptedByName && (
                        <span className="text-xs text-muted">
                          đã nhận: {inv.acceptedByName}
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-2.5">
                      <Badge variant={invitationStatusVariant(inv.status)}>
                        {inv.status}
                      </Badge>
                    </td>
                    <td className="px-4 py-2.5 text-muted">
                      {teamRoleLabel(inv.teamRole)}
                    </td>
                    <td className="px-4 py-2.5 text-muted">
                      {inv.inviterName ?? "—"}
                    </td>
                    <td className="px-4 py-2.5 text-muted">
                      {formatDateTime(inv.expiresAt)}
                    </td>
                    <td className="px-4 py-2.5">
                      <span className="flex flex-wrap gap-1.5">
                        {inv.link && (
                          <Button
                            size="sm"
                            variant="ghost"
                            onClick={() =>
                              navigator.clipboard.writeText(inv.link!)
                            }
                          >
                            <Copy className="size-3.5" aria-hidden /> Link
                          </Button>
                        )}
                        {(inv.status === "Chờ" || inv.status === "Hết hạn") && (
                          <>
                            <Button
                              size="sm"
                              variant="outline"
                              disabled={resend.isPending}
                              onClick={() =>
                                resend.mutate(inv.id, {
                                  onError: (e) =>
                                    alert(
                                      apiErrorTitle(e, "Không gửi lại được"),
                                    ),
                                })
                              }
                            >
                              <Send className="size-3.5" aria-hidden /> Gửi lại
                            </Button>
                            {inv.status === "Chờ" && (
                              <Button
                                size="sm"
                                variant="ghost"
                                className="text-wrong hover:text-wrong"
                                disabled={revoke.isPending}
                                onClick={() => {
                                  if (
                                    window.confirm(
                                      `Thu hồi lời mời gửi cho ${inv.email}?`,
                                    )
                                  )
                                    revoke.mutate(inv.id, {
                                      onError: (e) =>
                                        alert(
                                          apiErrorTitle(
                                            e,
                                            "Không thu hồi được",
                                          ),
                                        ),
                                    });
                                }}
                              >
                                <Undo2 className="size-3.5" aria-hidden /> Thu
                                hồi
                              </Button>
                            )}
                          </>
                        )}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

// ===== Thông báo tổ =====

function AnnouncementsTab({
  teamId,
  canManage,
}: {
  teamId: number;
  canManage: boolean;
}) {
  const { data: items, isPending } = useTeamAnnouncements(teamId);
  const post = usePostTeamAnnouncement(teamId);
  const [title, setTitle] = useState("");
  const [body, setBody] = useState("");
  const [pinned, setPinned] = useState(false);
  const [expire, setExpire] = useState("");
  const [error, setError] = useState<string | null>(null);

  function onSubmit() {
    setError(null);
    const expireAt = expire ? dayjs.tz(expire, TZ).toISOString() : null;
    post.mutate(
      { title, bodyHtml: body, isPinned: pinned, expireAt },
      {
        onSuccess: () => {
          setTitle("");
          setBody("");
          setPinned(false);
          setExpire("");
        },
        onError: (e) => setError(apiErrorTitle(e, "Không đăng được thông báo")),
      },
    );
  }

  return (
    <div className="space-y-5">
      {canManage && (
        <div className="rounded-card border border-grid bg-white p-4">
          <h2 className="font-medium">Đăng thông báo cho tổ</h2>
          <div className="mt-3 space-y-3">
            <Input
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="Tiêu đề (3–200 ký tự)"
              minLength={3}
              maxLength={200}
            />
            <textarea
              value={body}
              onChange={(e) => setBody(e.target.value)}
              rows={3}
              placeholder="Nội dung thông báo…"
              className="w-full rounded-btn border border-grid bg-white p-3 text-sm outline-none transition focus:border-violet focus:ring-2 focus:ring-violet/20"
            />
            <div className="flex flex-wrap items-center gap-4">
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={pinned}
                  onChange={(e) => setPinned(e.target.checked)}
                />
                Ghim nổi bật
              </label>
              <label className="flex items-center gap-2 text-sm">
                Hết hạn (tùy chọn)
                <input
                  type="datetime-local"
                  value={expire}
                  onChange={(e) => setExpire(e.target.value)}
                  className="h-9 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
                />
              </label>
              <Button
                disabled={
                  title.trim().length < 3 || !body.trim() || post.isPending
                }
                onClick={onSubmit}
              >
                {post.isPending ? (
                  <Loader2 className="size-4 animate-spin" aria-hidden />
                ) : (
                  <Megaphone className="size-4" aria-hidden />
                )}
                Đăng
              </Button>
            </div>
            {error && <p className="text-sm text-wrong">{error}</p>}
          </div>
        </div>
      )}

      {isPending ? (
        <TabLoading />
      ) : !items || items.length === 0 ? (
        <EmptyState
          icon={Megaphone}
          title="Chưa có thông báo nào"
          description={
            canManage
              ? "Đăng thông báo đầu tiên cho tổ phía trên."
              : "Chờ tổ trưởng/tổ phó đăng thông báo."
          }
        />
      ) : (
        <ul className="space-y-3">
          {items.map((a) => (
            <li
              key={a.id}
              className="rounded-card border border-grid bg-white p-4"
            >
              <div className="flex flex-wrap items-center gap-2">
                <p className="font-medium">{a.title}</p>
                {a.isPinned && <Badge variant="warning">Đã ghim</Badge>}
                <span className="ml-auto text-xs text-muted">
                  {a.authorName} · {formatDateTime(a.createdAt)}
                </span>
              </div>
              {/* bodyHtml đã được sanitize phía BE (spec §12) */}
              <div
                className="mt-2 text-sm text-ink/90"
                dangerouslySetInnerHTML={{ __html: a.bodyHtml }}
              />
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

// ===== Thống kê =====

function StatsTab({ teamId }: { teamId: number }) {
  const { data: stats, isPending } = useTeamStats(teamId);
  if (isPending) return <TabLoading />;
  if (!stats) return null;

  return (
    <div className="space-y-4">
      <div className="grid gap-3 sm:grid-cols-3">
        <StatCard label="Thành viên" value={stats.memberCount} />
        <StatCard label="Tài liệu" value={stats.documentCount} />
        <StatCard label="Bài tập" value={stats.quizCount} />
      </div>
      <div className="overflow-x-auto rounded-card border border-grid bg-white">
        <table className="w-full min-w-[480px] text-sm">
          <thead>
            <tr className="border-b border-grid text-left text-muted">
              <th className="px-4 py-2.5 font-medium">Thành viên</th>
              <th className="px-4 py-2.5 font-medium">Vai trò</th>
              <th className="px-4 py-2.5 text-right font-medium">Tài liệu</th>
              <th className="px-4 py-2.5 text-right font-medium">Bài tập</th>
            </tr>
          </thead>
          <tbody>
            {stats.members.map((m) => (
              <tr
                key={m.userId}
                className="border-b border-grid/60 last:border-0"
              >
                <td className="px-4 py-2.5">
                  <span className="flex items-center gap-2">
                    <Avatar name={m.fullName} />
                    {m.fullName}
                  </span>
                </td>
                <td className="px-4 py-2.5">
                  <Badge variant={teamRoleVariant(m.role)}>
                    {teamRoleLabel(m.role)}
                  </Badge>
                </td>
                <td className="px-4 py-2.5 text-right tabular-nums">
                  {m.documentCount}
                </td>
                <td className="px-4 py-2.5 text-right tabular-nums">
                  {m.quizCount}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ===== Phụ kiện nhỏ =====

function TabLoading() {
  return (
    <div className="flex justify-center py-12">
      <Loader2 className="size-5 animate-spin text-violet" aria-hidden />
    </div>
  );
}

function StatCard({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-card border border-grid bg-white p-4">
      <p className="text-sm text-muted">{label}</p>
      <p className="mt-1 text-2xl font-semibold tabular-nums">{value}</p>
    </div>
  );
}

function Notice({ text, onClear }: { text: string; onClear: () => void }) {
  return (
    <div
      role="status"
      className="mb-3 flex items-center justify-between gap-3 rounded-btn border border-correct/40 bg-correct/10 px-3 py-2 text-sm text-correct"
    >
      <span>{text}</span>
      <button
        type="button"
        aria-label="Đóng"
        onClick={onClear}
        className="text-correct/70 hover:text-correct"
      >
        <X className="size-4" aria-hidden />
      </button>
    </div>
  );
}

function Avatar({ src, name }: { src?: string | null; name: string }) {
  if (src)
    return (
      <img
        src={src}
        alt=""
        className="size-8 shrink-0 rounded-full object-cover"
      />
    );
  const initials = name
    .split(" ")
    .filter(Boolean)
    .slice(-2)
    .map((p) => p[0]?.toUpperCase())
    .join("");
  return (
    <span
      aria-hidden
      className="inline-flex size-8 shrink-0 items-center justify-center rounded-full bg-violet/10 text-xs font-semibold text-violet"
    >
      {initials || "?"}
    </span>
  );
}
