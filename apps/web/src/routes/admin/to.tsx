import { useState } from "react";
import { Loader2, Pencil, Search, UserCog, Users, X } from "lucide-react";
import { useTaxonomy } from "@/features/home/api";
import {
  useAdminCreateTeam,
  useAdminDeactivateTeam,
  useAdminSetLead,
  useAdminTeams,
  useAdminUpdateTeam,
  useAdminUsers,
} from "@/features/teams/api";
import type { TeamDto } from "@/features/teams/types";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { EmptyState } from "@/components/common/EmptyState";

interface GradeOption {
  id: number;
  name: string;
}

/** §5.4 /admin/to — CRUD tổ + bổ nhiệm tổ trưởng (người cũ thành Member). */
export function AdminToPage() {
  const { data: teams, isPending } = useAdminTeams();
  const { data: taxonomy } = useTaxonomy();
  const createTeam = useAdminCreateTeam();
  const updateTeam = useAdminUpdateTeam();
  const [form, setForm] = useState<
    { mode: "new" } | { mode: "edit"; team: TeamDto } | null
  >(null);
  const [error, setError] = useState<string | null>(null);

  if (isPending)
    return (
      <div className="flex justify-center py-16">
        <Loader2 className="size-6 animate-spin text-violet" aria-hidden />
      </div>
    );

  const grades: GradeOption[] = (taxonomy?.grades ?? []).map((g) => ({
    id: g.id,
    name: g.name,
  }));

  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold">Tổ chuyên môn</h1>
          <p className="mt-1 text-sm text-muted">{teams?.length ?? 0} tổ</p>
        </div>
        <Button onClick={() => setForm({ mode: "new" })}>Tạo tổ mới</Button>
      </div>

      {form && (
        <TeamForm
          key={form.mode === "edit" ? form.team.id : "new"}
          initial={form.mode === "edit" ? form.team : undefined}
          grades={grades}
          onCancel={() => {
            setForm(null);
            setError(null);
          }}
          onCreate={(body) =>
            createTeam.mutate(body, {
              onSuccess: () => setForm(null),
              onError: (e) => setError(e.message),
            })
          }
          onUpdate={(id, body) =>
            updateTeam.mutate(
              { id, body },
              {
                onSuccess: () => setForm(null),
                onError: (e) => setError(e.message),
              },
            )
          }
        />
      )}
      {error && <p className="mt-2 text-sm text-wrong">{error}</p>}

      {!teams || teams.length === 0 ? (
        <div className="mt-5">
          <EmptyState
            icon={Users}
            title="Chưa có tổ nào"
            description="Tạo tổ chuyên môn đầu tiên để giáo viên tham gia."
          />
        </div>
      ) : (
        <div className="mt-5 overflow-x-auto rounded-card border border-grid bg-white">
          <table className="w-full min-w-[760px] text-sm">
            <thead>
              <tr className="border-b border-grid text-left text-muted">
                <th className="px-4 py-2.5 font-medium">Tên tổ</th>
                <th className="px-4 py-2.5 font-medium">Khối</th>
                <th className="px-4 py-2.5 font-medium">Tổ trưởng</th>
                <th className="px-4 py-2.5 font-medium">Thành viên</th>
                <th className="px-4 py-2.5 font-medium">Trạng thái</th>
                <th className="px-4 py-2.5 font-medium">Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {teams.map((t) => (
                <TeamRow
                  key={t.id}
                  team={t}
                  onEdit={() => setForm({ mode: "edit", team: t })}
                />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function TeamRow({ team, onEdit }: { team: TeamDto; onEdit: () => void }) {
  const deactivate = useAdminDeactivateTeam();
  const [leadPickerOpen, setLeadPickerOpen] = useState(false);
  const [q, setQ] = useState("");
  const { data: users } = useAdminUsers({ q: q || undefined });

  return (
    <tr className="border-b border-grid/60 align-top last:border-0">
      <td className="px-4 py-2.5">
        <span className="block font-medium text-ink">{team.name}</span>
        {team.description && (
          <span className="text-xs text-muted">{team.description}</span>
        )}
      </td>
      <td className="px-4 py-2.5 text-muted">{team.gradeName ?? "—"}</td>
      <td className="px-4 py-2.5">
        {team.leadName ? (
          <span className="font-medium">{team.leadName}</span>
        ) : (
          <Badge variant="warning">Chưa có tổ trưởng</Badge>
        )}
      </td>
      <td className="px-4 py-2.5 tabular-nums">{team.memberCount}</td>
      <td className="px-4 py-2.5">
        {team.isActive ? (
          <Badge variant="success">Đang hoạt động</Badge>
        ) : (
          <Badge variant="outline">Ngừng hoạt động</Badge>
        )}
      </td>
      <td className="px-4 py-2.5">
        <span className="flex flex-wrap gap-1.5">
          <Button size="sm" variant="outline" onClick={onEdit}>
            <Pencil className="size-3.5" aria-hidden /> Sửa
          </Button>
          <Button
            size="sm"
            variant="outline"
            onClick={() => setLeadPickerOpen((v) => !v)}
          >
            <UserCog className="size-3.5" aria-hidden /> Tổ trưởng
          </Button>
          <Button
            size="sm"
            variant={team.isActive ? "ghost" : "secondary"}
            className={team.isActive ? "text-wrong hover:text-wrong" : ""}
            disabled={deactivate.isPending}
            onClick={() => {
              const ok = team.isActive
                ? window.confirm(
                    `Ngưng hoạt động tổ ${team.name}? Thành viên và nội dung không bị xóa.`,
                  )
                : window.confirm(`Mở lại hoạt động tổ ${team.name}?`);
              if (!ok) return;
              deactivate.mutate(team.id, {
                onError: (e) => alert(e.message),
              });
            }}
          >
            {team.isActive ? "Ngừng hoạt động" : "Mở lại"}
          </Button>
        </span>
        {leadPickerOpen && (
          <span className="mt-2 block w-full rounded-card border border-grid bg-paper p-3">
            <p className="text-xs font-medium text-muted">
              Bổ nhiệm tổ trưởng (tổ trưởng cũ thành thành viên)
            </p>
            <div className="relative mt-2">
              <Search
                className="pointer-events-none absolute left-2 top-1/2 size-4 -translate-y-1/2 text-muted"
                aria-hidden
              />
              <Input
                className="h-8 pl-8 text-xs"
                placeholder="Tìm theo họ tên hoặc email…"
                value={q}
                onChange={(e) => setQ(e.target.value)}
              />
            </div>
            <ul className="mt-2 max-h-40 space-y-1 overflow-y-auto">
              {(users ?? []).slice(0, 20).map((u) => (
                <LeadPickRow
                  key={u.id}
                  teamId={team.id}
                  userId={u.id}
                  name={u.fullName}
                  email={u.email}
                  disabled={u.status !== "Active"}
                  onPicked={() => setLeadPickerOpen(false)}
                />
              ))}
              {(users ?? []).length === 0 && (
                <li className="text-xs text-muted">
                  {q ? "Không tìm thấy giáo viên nào." : "Gõ để tìm giáo viên."}
                </li>
              )}
            </ul>
          </span>
        )}
      </td>
    </tr>
  );
}

function LeadPickRow({
  teamId,
  userId,
  name,
  email,
  disabled,
  onPicked,
}: {
  teamId: number;
  userId: number;
  name: string;
  email: string;
  disabled: boolean;
  onPicked: () => void;
}) {
  const setLead = useAdminSetLead();
  return (
    <li>
      <button
        type="button"
        disabled={disabled || setLead.isPending}
        onClick={() =>
          setLead.mutate(
            { id: teamId, body: { userId } },
            {
              onSuccess: onPicked,
              onError: (e) => alert(e.message),
            },
          )
        }
        className="block w-full rounded-btn px-2 py-1.5 text-left text-sm transition hover:bg-white disabled:opacity-50"
      >
        <span className="font-medium">{name}</span>
        <span className="block text-xs text-muted">
          {email}
          {disabled && " · chưa Active"}
        </span>
      </button>
    </li>
  );
}

function TeamForm({
  initial,
  grades,
  onCreate,
  onUpdate,
  onCancel,
}: {
  initial?: TeamDto;
  grades: GradeOption[];
  onCreate: (body: {
    name: string;
    description?: string | null;
    gradeId?: number | null;
  }) => void;
  onUpdate: (
    id: number,
    body: {
      name?: string;
      description?: string | null;
      gradeId?: number | null;
    },
  ) => void;
  onCancel: () => void;
}) {
  const [name, setName] = useState(initial?.name ?? "");
  const [description, setDescription] = useState(initial?.description ?? "");
  const [gradeId, setGradeId] = useState("0");
  const [err, setErr] = useState<string | null>(null);

  function submit() {
    setErr(null);
    if (name.trim().length < 2 || name.trim().length > 100) {
      setErr("Tên tổ phải từ 2 đến 100 ký tự.");
      return;
    }
    const gradeValue = gradeId === "0" ? null : Number(gradeId);
    if (initial)
      onUpdate(initial.id, {
        name: name.trim(),
        description: description.trim() || null,
        gradeId: gradeValue,
      });
    else
      onCreate({
        name: name.trim(),
        description: description.trim() || null,
        gradeId: gradeValue,
      });
  }

  return (
    <div className="mt-4 rounded-card border border-violet/40 bg-white p-4">
      <div className="flex items-center justify-between">
        <h2 className="font-medium">
          {initial ? `Sửa tổ: ${initial.name}` : "Tạo tổ mới"}
        </h2>
        <button
          type="button"
          aria-label="Đóng"
          onClick={onCancel}
          className="text-muted hover:text-ink"
        >
          <X className="size-4" aria-hidden />
        </button>
      </div>
      <div className="mt-3 grid gap-3 md:grid-cols-2">
        <label className="block">
          <span className="mb-1 block text-sm font-medium text-ink">
            Tên tổ *
          </span>
          <Input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Tổ Toán"
            maxLength={100}
          />
        </label>
        <label className="block">
          <span className="mb-1 block text-sm font-medium text-ink">
            Khối phụ trách
          </span>
          <select
            value={gradeId}
            onChange={(e) => setGradeId(e.target.value)}
            className="h-10 w-full rounded-btn border border-grid bg-white px-3 text-sm outline-none transition focus:border-violet focus:ring-2 focus:ring-violet/20"
          >
            <option value="0">— Không gán khối —</option>
            {grades.map((g) => (
              <option key={g.id} value={g.id}>
                {g.name}
              </option>
            ))}
          </select>
        </label>
        <label className="block md:col-span-2">
          <span className="mb-1 block text-sm font-medium text-ink">Mô tả</span>
          <textarea
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            rows={2}
            className="w-full rounded-btn border border-grid bg-white p-3 text-sm outline-none transition focus:border-violet focus:ring-2 focus:ring-violet/20"
          />
        </label>
      </div>
      {err && <p className="mt-2 text-sm text-wrong">{err}</p>}
      <div className="mt-3">
        <Button onClick={submit}>{initial ? "Lưu thay đổi" : "Tạo tổ"}</Button>
      </div>
    </div>
  );
}
