import { useState } from "react";
import { Copy, Loader2, Send } from "lucide-react";
import { apiErrorTitle } from "@/features/auth/api";
import {
  invitationResultInfo,
  invitationStatusVariant,
  parseEmails,
  teamRoleLabel,
} from "@/features/teams/labels";
import {
  useAdminCreateInvitations,
  useAdminInvitations,
  useAdminTeams,
} from "@/features/teams/api";
import type { TeamRole } from "@/features/auth/types";
import { formatDateTime } from "@/lib/date";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { EmptyState } from "@/components/common/EmptyState";

const INVITE_STATUSES = ["Chờ", "Đã nhận", "Hết hạn", "Đã thu hồi"];

/** §5.4 /admin/loi-moi — mọi lời mời + mời vào bất kỳ tổ/vai trò nào. */
export function AdminLoiMoiPage() {
  const { data: teams } = useAdminTeams();
  const [teamFilter, setTeamFilter] = useState("0");
  const [statusFilter, setStatusFilter] = useState("");
  const invites = useAdminInvitations({
    teamId: teamFilter === "0" ? undefined : Number(teamFilter),
    status: statusFilter || undefined,
  });
  const create = useAdminCreateInvitations();
  const [emailsText, setEmailsText] = useState("");
  const [role, setRole] = useState<TeamRole>("Member");
  const [message, setMessage] = useState("");
  const [results, setResults] = useState<
    { email: string; label: string; tone: "ok" | "info" | "warn"; link: string | null }[]
  >([]);
  const [error, setError] = useState<string | null>(null);

  const validEmails = parseEmails(emailsText);

  function onInvite() {
    setError(null);
    if (teamFilter === "0") {
      setError("Chọn tổ nhận lời mời.");
      return;
    }
    create.mutate(
      {
        teamId: Number(teamFilter),
        teamRole: role,
        emails: validEmails,
        message: message || null,
      },
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

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-xl font-semibold">Lời mời</h1>
        <p className="mt-1 text-sm text-muted">
          Mời vào bất kỳ tổ nào với vai trò bất kỳ (kể cả tổ trưởng).
        </p>
      </div>

      <div className="rounded-card border border-grid bg-white p-4">
        <h2 className="font-medium">Gửi lời mời</h2>
        <div className="mt-3 grid gap-3 md:grid-cols-2">
          <label className="block">
            <span className="mb-1 block text-sm font-medium text-ink">
              Tổ nhận
            </span>
            <select
              value={teamFilter}
              onChange={(e) => setTeamFilter(e.target.value)}
              className="h-10 w-full rounded-btn border border-grid bg-white px-3 text-sm outline-none transition focus:border-violet focus:ring-2 focus:ring-violet/20"
            >
              <option value="0">— Chọn tổ —</option>
              {(teams ?? []).map((t) => (
                <option key={t.id} value={t.id}>
                  {t.name}
                  {t.isActive ? "" : " (ngừng hoạt động)"}
                </option>
              ))}
            </select>
          </label>
          <label className="block">
            <span className="mb-1 block text-sm font-medium text-ink">
              Vai trò
            </span>
            <select
              value={role}
              onChange={(e) => setRole(e.target.value as TeamRole)}
              className="h-10 w-full rounded-btn border border-grid bg-white px-3 text-sm outline-none transition focus:border-violet focus:ring-2 focus:ring-violet/20"
            >
              <option value="Member">Thành viên</option>
              <option value="Deputy">Tổ phó</option>
              <option value="Lead">Tổ trưởng</option>
            </select>
          </label>
          <label className="block md:col-span-2">
            <span className="mb-1 block text-sm font-medium text-ink">
              Email ({validEmails.length} hợp lệ)
            </span>
            <textarea
              value={emailsText}
              onChange={(e) => setEmailsText(e.target.value)}
              rows={3}
              placeholder={"mỗi email một dòng\nhoặc phân tách bằng dấu phẩy"}
              className="w-full rounded-btn border border-grid bg-white p-3 text-sm outline-none transition focus:border-violet focus:ring-2 focus:ring-violet/20"
            />
          </label>
          <label className="block md:col-span-2">
            <span className="mb-1 block text-sm font-medium text-ink">
              Lời nhắn (tùy chọn)
            </span>
            <Input
              value={message}
              onChange={(e) => setMessage(e.target.value)}
              maxLength={300}
            />
          </label>
        </div>
        {error && <p className="mt-2 text-sm text-wrong">{error}</p>}
        <div className="mt-3 flex flex-wrap items-center gap-2">
          <Button
            disabled={
              validEmails.length === 0 ||
              teamFilter === "0" ||
              create.isPending
            }
            onClick={onInvite}
          >
            {create.isPending ? (
              <Loader2 className="size-4 animate-spin" aria-hidden />
            ) : (
              <Send className="size-4" aria-hidden />
            )}
            Gửi lời mời
          </Button>
          <span className="text-xs text-warn">
            Mời làm tổ trưởng sẽ hạ tổ trưởng hiện tại xuống thành viên.
          </span>
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
        <div className="mb-2 flex flex-wrap items-center gap-2">
          <h2 className="font-medium">Mọi lời mời (500 mới nhất)</h2>
          <select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            aria-label="Lọc theo trạng thái"
            className="h-9 rounded-btn border border-grid bg-white px-2 text-sm outline-none focus:border-violet"
          >
            <option value="">Mọi trạng thái</option>
            {INVITE_STATUSES.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </select>
        </div>
        {invites.isPending ? (
          <div className="flex justify-center py-12">
            <Loader2 className="size-5 animate-spin text-violet" aria-hidden />
          </div>
        ) : !invites.data || invites.data.length === 0 ? (
          <EmptyState
            icon={Send}
            title="Chưa có lời mời nào"
            description="Gửi lời mời đầu tiên bằng form phía trên."
          />
        ) : (
          <div className="overflow-x-auto rounded-card border border-grid bg-white">
            <table className="w-full min-w-[820px] text-sm">
              <thead>
                <tr className="border-b border-grid text-left text-muted">
                  <th className="px-4 py-2.5 font-medium">Email</th>
                  <th className="px-4 py-2.5 font-medium">Tổ</th>
                  <th className="px-4 py-2.5 font-medium">Vai trò</th>
                  <th className="px-4 py-2.5 font-medium">Trạng thái</th>
                  <th className="px-4 py-2.5 font-medium">Người mời</th>
                  <th className="px-4 py-2.5 font-medium">Hết hạn</th>
                  <th className="px-4 py-2.5 font-medium">Link</th>
                </tr>
              </thead>
              <tbody>
                {invites.data.map((inv) => (
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
                    <td className="px-4 py-2.5 text-muted">
                      {inv.teamName ?? "—"}
                    </td>
                    <td className="px-4 py-2.5 text-muted">
                      {teamRoleLabel(inv.teamRole)}
                    </td>
                    <td className="px-4 py-2.5">
                      <Badge variant={invitationStatusVariant(inv.status)}>
                        {inv.status}
                      </Badge>
                    </td>
                    <td className="px-4 py-2.5 text-muted">
                      {inv.inviterName ?? "—"}
                    </td>
                    <td className="px-4 py-2.5 text-muted">
                      {formatDateTime(inv.expiresAt)}
                    </td>
                    <td className="px-4 py-2.5">
                      {inv.link ? (
                        <Button
                          size="sm"
                          variant="ghost"
                          onClick={() =>
                            navigator.clipboard.writeText(inv.link!)
                          }
                        >
                          <Copy className="size-3.5" aria-hidden /> Sao chép
                        </Button>
                      ) : (
                        <span className="text-xs text-muted">—</span>
                      )}
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
