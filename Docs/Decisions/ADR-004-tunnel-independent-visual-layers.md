# ADR-004: Tunnel uses independent frame and shutter layers

## Status

Accepted.

## Context

`map.png` provides a U-shaped Tunnel with no bottom rail, while `TunnelShutter.png` is a separately authored rounded rectangular panel. Treating the Tunnel as a generic four-sided 9-slice aperture and fitting the shutter to that aperture produced a mirrored bottom frame, exposed dark backing at lower corners, and a nested rounded frame.

## Decision

The static map Tunnel is the only frame renderer and imports with a Full Rect mesh so its 9-sliced U geometry is reliable. The backing, shutter mesh, and preview mask use direct reveal-area sizing plus dedicated panel configuration; none derive their dimensions from the frame border. The shutter shader samples only the flat slatted interior of its source artwork. The preview mask uses the opaque rectangular backing sprite.

## Consequences

This preserves a clean open bottom at every supported Tunnel size and keeps the existing progressive shutter opening independent of gameplay. Asset regeneration must retain this exact layer ownership. Changes to the shutter artwork require reviewing its configured interior UV rectangle, while changes to the frame artwork require reviewing only its U-frame slicing.
