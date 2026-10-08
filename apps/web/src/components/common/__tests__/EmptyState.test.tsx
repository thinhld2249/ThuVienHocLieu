import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { PencilLine } from "lucide-react";
import { EmptyState } from "../EmptyState";

describe("EmptyState", () => {
  it("hiện tiêu đề, mô tả và hành động", () => {
    render(
      <EmptyState
        icon={PencilLine}
        title="Chưa có bài tập nào"
        description="Bạn có thể tạo bài tập từ file Word."
        action={<button type="button">Tạo từ file Word</button>}
      />,
    );
    expect(
      screen.getByRole("heading", { name: "Chưa có bài tập nào" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Bạn có thể tạo bài tập từ file Word."),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Tạo từ file Word" }),
    ).toBeInTheDocument();
  });
});
