export function projectImageUrl(url) {
    const resolve = window.parent.novalistResolveProjectAsset;
    return resolve ? resolve(url) : Promise.resolve(url);
}
