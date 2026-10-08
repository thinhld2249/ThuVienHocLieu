import { Navigate, useNavigate } from "react-router";
import { Loader2, Users } from "lucide-react";
import { useMe } from "@/features/auth/api";
import { teamRoleLabel, teamRoleVariant } from "@/features/teams/labels";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/common/EmptyState";

/**
 * §5.2 /gv/to — không gian tổ:
 * 0 tổ → màn trống có hành động · 1 tổ → vào thẳng · >1 tổ → lưới chọn.
 */
export function ToPage() {
  const { data: me, isPending } = useMe();
  const navigate = useNavigate();

  if (isPending)
    return (
      <div className="flex justify-center py-16">
        <Loader2 className="size-6 animate-spin text-violet" aria-hidden />
      </div>
    );
  if (!me) return <Navigate to="/dang-nhap" replace />;

  const teams = me.teams;
  if (teams.length === 0) {
    const isAdmin = me.systemRole === "Admin";
    return (
      <EmptyState
        icon={Users}
        title={
          isAdmin
            ? "Chưa có tổ chuyên môn nào"
            : "Bạn chưa tham gia tổ chuyên môn nào"
        }
        description={
          isAdmin
            ? "Tạo tổ chuyên môn đầu tiên ở khu Quản trị, sau đó mời giáo viên vào."
            : "Liên hệ quản trị viên trường để được mời vào tổ. Khi nhận được lời mời, đăng nhập lại và quay về đây."
        }
        action={
          isAdmin ? (
            <Button onClick={() => navigate("/admin/to")}>Tạo tổ mới</Button>
          ) : undefined
        }
      />
    );
  }
  if (teams.length === 1)
    return <Navigate to={`/gv/to/${teams[0].id}`} replace />;

  return (
    <div>
      <h1 className="text-xl font-semibold">Chọn tổ chuyên môn</h1>
      <p className="mt-1 text-sm text-muted">
        Bạn thuộc {teams.length} tổ. Chọn một tổ để mở không gian làm việc.
      </p>
      <div className="mt-5 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {teams.map((t) => (
          <button
            key={t.id}
            type="button"
            onClick={() => navigate(`/gv/to/${t.id}`)}
            className="group rounded-card border border-grid bg-white p-4 text-left transition hover:border-violet/50"
          >
            <div className="flex items-center justify-between gap-2">
              <p className="font-medium group-hover:text-violet">{t.name}</p>
              <Badge variant={teamRoleVariant(t.role)}>
                {teamRoleLabel(t.role)}
              </Badge>
            </div>
          </button>
        ))}
      </div>
    </div>
  );
}
