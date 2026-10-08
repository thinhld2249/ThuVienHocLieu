import type { LucideIcon } from "lucide-react";
import type { ReactNode } from "react";

/** Màn trống luôn có hành động (spec §14.4). */
export function EmptyState({
  icon: Icon,
  title,
  description,
  action,
  className,
}: {
  icon?: LucideIcon;
  title: string;
  description?: string;
  action?: ReactNode;
  className?: string;
}) {
  return (
    <div
      className={`flex flex-col items-center justify-center gap-2 rounded-card border border-dashed border-grid bg-white/60 px-6 py-12 text-center ${className ?? ""}`}
    >
      {Icon ? <Icon className="size-8 text-muted/60" aria-hidden /> : null}
      <h2 className="font-medium">{title}</h2>
      {description ? (
        <p className="max-w-md text-sm text-muted">{description}</p>
      ) : null}
      {action ? <div className="mt-2">{action}</div> : null}
    </div>
  );
}
