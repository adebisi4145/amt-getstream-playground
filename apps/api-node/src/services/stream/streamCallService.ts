import { randomUUID } from "node:crypto";
import type { StreamClient } from "@stream-io/node-sdk";
import { callStream } from "./streamRequestFailedError.ts";
import type { StreamUserService } from "./streamUserService.ts";

/**
 * Both kinds use Stream's `default` call type, which supports ringing. The kind is carried in `custom.kind`,
 * and clients keep the camera off for audio calls, the way apps/web does. The call's own video settings are
 * left alone: Stream validates `settings_override.video` as a whole, so a partial override is rejected, and
 * sending the block wholesale turns `enabled` off and stops a call being escalated to video later.
 */
export const CALL_TYPE = "default";

export type CallKind = "video" | "audio";

export interface StartCallInput {
  createdById: string;
  /** Users to call. The creator is added as a member too, so they can join and end the call. */
  memberIds: readonly string[];
  kind: CallKind;
  /** Sends a ring event (incoming call) to members who aren't in the call yet. */
  ring: boolean;
}

export interface StartedCall {
  callType: string;
  callId: string;
  cid: string;
}

export interface CallMember {
  userId: string;
  name: string | undefined;
  image: string | undefined;
}

export interface CallDetails extends StartedCall {
  kind: CallKind;
  createdById: string;
  createdAt: string;
  endedAt: string | undefined;
  /** True while someone is in the call. */
  live: boolean;
  members: CallMember[];
  acceptedBy: string[];
  rejectedBy: string[];
  missedBy: string[];
}

export interface StreamCallService {
  startCall(input: StartCallInput): Promise<StartedCall>;

  /** Reads a call's state. Throws a not-found Stream failure when the call doesn't exist. */
  getCall(callId: string): Promise<CallDetails>;

  /** Ends the call for everyone. */
  endCall(callId: string): Promise<void>;
}

export function createStreamCallService(client: StreamClient, users: StreamUserService): StreamCallService {
  return {
    async startCall({ createdById, memberIds, kind, ring }) {
      const allMemberIds = [...new Set([createdById, ...memberIds])];

      // Stream rejects members that don't exist yet, so create any missing users (id only, nothing else changes).
      await users.ensureUsersExist(allMemberIds);

      const call = client.video.call(CALL_TYPE, randomUUID());
      const video = kind === "video";

      await callStream("startCall", () =>
        call.getOrCreate({
          ring,
          video,
          data: {
            created_by_id: createdById,
            members: allMemberIds.map((user_id) => ({ user_id })),
            video,
            custom: { kind },
          },
        }),
      );

      return { callType: call.type, callId: call.id, cid: call.cid };
    },

    async getCall(callId) {
      const call = client.video.call(CALL_TYPE, callId);
      const { call: details, members } = await callStream("getCall", () => call.get());
      const session = details.session;

      return {
        callType: call.type,
        callId: call.id,
        cid: call.cid,
        kind: details.custom.kind === "audio" ? "audio" : "video",
        createdById: details.created_by.id,
        createdAt: details.created_at.toISOString(),
        endedAt: details.ended_at?.toISOString(),
        live: (session?.participants.length ?? 0) > 0,
        members: members.map((member) => ({
          userId: member.user_id,
          name: member.user.name,
          image: member.user.image,
        })),
        acceptedBy: Object.keys(session?.accepted_by ?? {}),
        rejectedBy: Object.keys(session?.rejected_by ?? {}),
        missedBy: Object.keys(session?.missed_by ?? {}),
      };
    },

    async endCall(callId) {
      await callStream("endCall", () => client.video.call(CALL_TYPE, callId).end());
    },
  };
}
