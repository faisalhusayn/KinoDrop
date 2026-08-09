import ActivityKit
import SwiftUI
import WidgetKit

struct KinoDropLiveActivity: Widget {
    var body: some WidgetConfiguration {
        ActivityConfiguration(for: TransferActivityAttributes.self) { context in
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Image(systemName: context.attributes.direction == "upload" ? "arrow.up.circle.fill" : "arrow.down.circle.fill")
                    Text(context.attributes.fileName)
                        .lineLimit(1)
                    Spacer()
                    Text(context.state.phase)
                        .font(.caption)
                }

                if let progress = context.state.progress {
                    ProgressView(value: progress)
                    Text("\(Int(progress * 100))%")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                } else {
                    ProgressView()
                }
            }
            .padding()
            .activityBackgroundTint(.black)
            .activitySystemActionForegroundColor(.white)
        } dynamicIsland: { context in
            DynamicIsland {
                DynamicIslandExpandedRegion(.leading) {
                    Image(systemName: context.attributes.direction == "upload" ? "arrow.up" : "arrow.down")
                }
                DynamicIslandExpandedRegion(.center) {
                    Text(context.attributes.fileName)
                        .lineLimit(1)
                }
                DynamicIslandExpandedRegion(.bottom) {
                    VStack(alignment: .leading, spacing: 4) {
                        if let progress = context.state.progress {
                            ProgressView(value: progress)
                            Text("\(Int(progress * 100))% · \(context.state.phase)")
                                .font(.caption2)
                        } else {
                            ProgressView()
                            Text(context.state.phase)
                                .font(.caption2)
                        }
                    }
                }
            } compactLeading: {
                Image(systemName: context.attributes.direction == "upload" ? "arrow.up" : "arrow.down")
            } compactTrailing: {
                if let progress = context.state.progress {
                    Text("\(Int(progress * 100))%")
                        .font(.caption2)
                } else {
                    ProgressView()
                }
            } minimal: {
                Image(systemName: context.attributes.direction == "upload" ? "arrow.up" : "arrow.down")
            }
        }
    }
}

@main
struct KinoDropLiveActivityBundle: WidgetBundle {
    var body: some Widget {
        KinoDropLiveActivity()
    }
}
